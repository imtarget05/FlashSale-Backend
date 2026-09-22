using FlashSale.Application.Inventory;
using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Application.Persistence;
using FlashSale.Infrastructure.Messaging;
using FlashSale.Infrastructure.Persistence;
using FlashSale.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------
// Composition root: PostgreSQL stays the source of truth; Redis is a
// fast pre-filter; the queue decouples acceptance from fulfillment.
// ---------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=FlashSaleDb;Username=postgres;Password=postgres;Maximum Pool Size=80";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderReadModel, OrderReadModel>();
builder.Services.AddScoped<IDatabaseHealthCheck, DatabaseHealthCheck>();

// ---------------------------------------------------------------
// Migration entrypoint (Phase 7C): `dotnet Order.Api.dll --migrate`.
//
// Positioned HERE, before Redis and the queue are registered, because a
// migration must depend on PostgreSQL and nothing else. Two reasons this is not
// cosmetic:
//   * ConnectionMultiplexer.Connect() below is eager, so a Redis-less Job would
//     hang/throw before reaching the migration.
//   * AddOrderQueue() is eager too and fails fast on missing broker config, so
//     placing the branch after it would make the migration Job unrunnable in
//     exactly the environment it exists for.
// infrastructure/kubernetes/base/migration/job.yaml is the only intended caller;
// the API and worker keep calling InitializeAsync for single-process local runs.
// ---------------------------------------------------------------
if (args.Contains("--migrate"))
{
    // ASP0000 is intentional: the migration path must run before the app (and
    // therefore before the queue/Redis singletons) exists, so a throwaway
    // provider is the only way to obtain a logger.
#pragma warning disable ASP0000
    await using var migrationProvider = builder.Services.BuildServiceProvider();
#pragma warning restore ASP0000
    var migrationLogger = migrationProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Migration");

    var exitCode = await MigrationRunner.RunAsync(migrationProvider, migrationLogger);
    migrationLogger.LogInformation("Migration entrypoint finished with exit code {ExitCode}", exitCode);
    return exitCode;
}

// Redis: enabled when reachable, transparently skipped otherwise.
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddSingleton<IStockReservationGateway>(sp =>
    new RedisStockGateway(sp.GetRequiredService<IConnectionMultiplexer>()));

// Queue selection: exactly one provider from configuration (ADR-005).
builder.Services.AddOrderQueue(builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddScoped<OrderProcessor>();
builder.Services.AddScoped<SubmitOrderUseCase>();
builder.Services.AddHostedService<OrderProcessorHost>();

var app = builder.Build();

// Startup concerns live in Infrastructure (schema + seed + Redis mirroring).
await DatabaseInitializer.InitializeAsync(app.Services);


// ---------------------------------------------------------------
// Thin HTTP endpoints: map routes and delegate to Application ports/use cases.
// No business logic here — only HTTP-to-domain translation.
// ---------------------------------------------------------------

app.MapPost("/api/orders", async (
    OrderRequest request,
    HttpRequest http,
    SubmitOrderUseCase useCase) =>
{
    var idempotencyKey = http.Headers.TryGetValue("Idempotency-Key", out var headerKey)
        ? headerKey.ToString()
        : Guid.NewGuid().ToString("N");

    var result = await useCase.ExecuteAsync(request.ProductId, request.Quantity, idempotencyKey);

    return result.Outcome switch
    {
        SubmitOrderOutcome.Accepted =>
            Results.Accepted(
                $"/api/orders/{result.IdempotencyKey}",
                new { message = "Order accepted", idempotencyKey = result.IdempotencyKey, status = "processing" }),
        SubmitOrderOutcome.DuplicateRequest =>
            Results.Json(new { error = "Duplicate request", idempotencyKey = result.IdempotencyKey }, statusCode: 409),
        SubmitOrderOutcome.OutOfStock =>
            Results.Json(new { error = "Out of stock" }, statusCode: 409),
        SubmitOrderOutcome.ProductNotFound =>
            Results.NotFound(new { error = "Product not found" }),
        SubmitOrderOutcome.SystemBusy =>
            Results.Json(new { error = "System busy, retry shortly" }, statusCode: 503),
        SubmitOrderOutcome.CompletedSynchronously =>
            Results.Ok(new { message = "Order placed successfully", idempotencyKey = result.IdempotencyKey }),
        SubmitOrderOutcome.InvalidQuantity =>
            Results.BadRequest(new { error = "Quantity must be positive" }),
        _ => Results.StatusCode(500)
    };
});

// Status polling: did my (idempotent) order actually persist?
app.MapGet("/api/orders/{idempotencyKey}", async (string idempotencyKey, IOrderReadModel readModel) =>
{
    var order = await readModel.GetOrderStatusAsync(idempotencyKey);
    return order is null
        ? Results.Json(new { idempotencyKey, status = "processing" })
        : Results.Json(new { order.IdempotencyKey, status = "completed", order.OrderId, order.ProductId, order.Quantity });
});

// Read endpoint for audits, dashboards and the benchmark harness.
app.MapGet("/api/products/{id}", async (int id, IOrderReadModel readModel) =>
{
    var product = await readModel.GetProductAsync(id);
    return product is null ? Results.NotFound() : Results.Ok(product);
});

// Ops runbook: rebuild the reservation counter from PostgreSQL truth after drift.
app.MapPost("/internal/resync-stock/{id}", async (int id, IOrderReadModel readModel, IStockReservationGateway redis) =>
{
    var product = await readModel.GetProductAsync(id);
    if (product is null) return Results.NotFound();
    await redis.SetStockAsync(id, product.AvailableStock);
    return Results.Ok(new { id, resyncedTo = product.AvailableStock });
});

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));

// Back-compat alias for compose probes written before the split.
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

// Readiness: can this instance safely receive traffic? Postgres is the source
// of truth so it gates readiness. Redis/RabbitMQ are RUNTIME OPTIONAL — the
// API falls back to synchronous Postgres processing when Redis is down
// (Task 2 verified), so their absence must NOT make the API unready.
app.MapGet("/health/ready", async (IDatabaseHealthCheck healthCheck, ILogger<Program> readinessLogger) =>
{
    var canConnect = await healthCheck.CanConnectAsync();
    if (!canConnect)
        readinessLogger.LogWarning("Readiness check failed: PostgreSQL unreachable.");

    return canConnect
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "not-ready" }, statusCode: 503);
});

app.Run();

// Explicit, because the --migrate branch above returns an int: top-level
// statements require every code path to return a value once one of them does.
return 0;

public record OrderRequest(int ProductId, int Quantity);
