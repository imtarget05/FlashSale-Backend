using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Application.Persistence;
using FlashSale.Domain;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Messaging;
using FlashSale.Infrastructure.Messaging;
using FlashSale.Infrastructure.Persistence;
using FlashSale.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
// Local: RabbitMQ (docker compose). Azure/AKS: RabbitMQ or Service Bus.
// InMemory is opt-in only (tests / single-process dev) and is refused when the
// host environment is Production — which is the ASP.NET default when
// ASPNETCORE_ENVIRONMENT is unset, i.e. how both AKS containers run. A
// per-process broker cannot connect two pods, so failing here is the only way
// to avoid a silently stuck pipeline. Unknown/incomplete config fails fast.
builder.Services.AddOrderQueue(builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddScoped<OrderProcessor>();
builder.Services.AddHostedService<OrderProcessorHost>();

var kafkaConnectionString = builder.Configuration.GetConnectionString("Kafka");
if (!string.IsNullOrEmpty(kafkaConnectionString))
{
    builder.Services.AddSingleton<KafkaActivityTracker>(sp =>
        new KafkaActivityTracker(kafkaConnectionString, sp.GetRequiredService<ILogger<KafkaActivityTracker>>()));
    builder.Services.AddHostedService(sp => sp.GetRequiredService<KafkaActivityTracker>());
}

var app = builder.Build();

// Startup concerns live in Infrastructure (schema + seed + Redis mirroring).
await DatabaseInitializer.InitializeAsync(app.Services);


app.MapPost("/api/orders", async (
    OrderRequest request,
    HttpRequest http,
    IOrderReadModel readModel,
    IStockReservationGateway redis,
    IOrderQueueProducer queue,
    OrderProcessor processor,
    ILogger<Program> logger) =>
{
    if (request.Quantity <= 0)
        return Results.BadRequest(new { error = "Quantity must be positive" });

    var idempotencyKey = http.Headers.TryGetValue("Idempotency-Key", out var headerKey)
        ? headerKey.ToString()
        : Guid.NewGuid().ToString("N");

    var message = new OrderMessage(request.ProductId, request.Quantity, idempotencyKey, DateTimeOffset.UtcNow);

    // T1 — fast reservation (also deduplicates by idempotency key).
    var reservation = await redis.TryReserveAsync(request.ProductId, request.Quantity, idempotencyKey);
    if (reservation == ReservationResult.Duplicate)
        return Results.Json(new { error = "Duplicate request", idempotencyKey }, statusCode: 409);
    if (reservation == ReservationResult.SoldOut)
        return Results.Json(new { error = "Out of stock" }, statusCode: 409);

    // T1b — unknown product: seed the reservation tier from the DB once, then retry.
    if (reservation == ReservationResult.UnknownProduct)
    {
        var stock = await readModel.GetStockAsync(request.ProductId);
        if (stock is null)
            return Results.NotFound(new { error = "Product not found" });

        await redis.SetStockAsync(request.ProductId, stock.Value);
        reservation = await redis.TryReserveAsync(request.ProductId, request.Quantity, idempotencyKey);
        if (reservation == ReservationResult.SoldOut)
            return Results.Json(new { error = "Out of stock" }, statusCode: 409);
    }

    if (reservation == ReservationResult.Reserved)
    {
        // T2 — hand off to the fulfillment pipeline.
        if (await queue.EnqueueAsync(message))
        {
            return Results.Accepted(
                $"/api/orders/{idempotencyKey}",
                new { message = "Order accepted", idempotencyKey, status = "processing" });
        }

        // Queue full -> backpressure: give the reservation back and shed load.
        await redis.ReleaseReservationAsync(request.ProductId, request.Quantity, idempotencyKey);
        return Results.Json(new { error = "System busy, retry shortly" }, statusCode: 503);
    }

    // T1c — reservation tier unavailable: fall back to the synchronous use case
    // (Phase 3 path; still idempotent thanks to the repository constraint).
    logger.LogWarning("Reservation tier unavailable ({Reservation}) — falling back to the synchronous path.", reservation);
    try
    {
        await processor.ProcessAsync(message, CancellationToken.None);
        return Results.Ok(new { Message = "Order placed successfully", IdempotencyKey = idempotencyKey });
    }
    catch (StockDriftException)
    {
        return Results.Json(new { error = "Out of stock" }, statusCode: 409);
    }
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
//
// Phase 4 gate finding: CanConnectAsync() returns FALSE for an unreachable
// server and only THROWS for some failure modes. The previous version ignored
// the boolean, so a database-less instance still answered {"status":"ready"} —
// a false-ready pod would receive traffic in Kubernetes. Both outcomes must now
// map to 503 not-ready.
app.MapGet("/health/ready", async (AppDbContext db, ILogger<Program> readinessLogger) =>
{
    bool canConnect;
    try
    {
        canConnect = await db.Database.CanConnectAsync();
    }
    catch (Exception ex)
    {
        readinessLogger.LogWarning(ex, "Readiness check failed: PostgreSQL unreachable.");
        canConnect = false;
    }

    return canConnect
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "not-ready" }, statusCode: 503);
});

app.Run();

// Explicit, because the --migrate branch above returns an int: top-level
// statements require every code path to return a value once one of them does.
return 0;

public record OrderRequest(int ProductId, int Quantity);
