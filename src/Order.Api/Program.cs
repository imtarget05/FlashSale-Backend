using FlashSale.Application.Auth;
using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Application.Persistence;
using FlashSale.Domain;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Messaging;
using FlashSale.Infrastructure.Auth;
using FlashSale.Infrastructure.Messaging;
using FlashSale.Infrastructure.Persistence;
using FlashSale.Infrastructure.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Order.Api;
using Order.Api.Auth;
using Order.Api.OpenApi;
using StackExchange.Redis;
using System.Security.Claims;
using System.Text;

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
// Observability (Phase III).
//
// ApiMetrics owns real System.Diagnostics.Metrics instruments; the singleton is
// what starts the MeterListener that aggregates them for the JSON snapshot.
//
// AddOpenApi() costs no new package: Microsoft.AspNetCore.OpenApi already ships
// in the ASP.NET shared framework and is referenced by this project.
// ---------------------------------------------------------------
builder.Services.AddSingleton<ApiMetrics>();
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

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

// ---------------------------------------------------------------
// Authentication & authorization (ADR-013).
//
// Registered AFTER the --migrate branch, and that ordering is load-bearing:
// JwtOptions.FromConfiguration() fails fast when a signing key is missing, and
// the whole point of the migration Job is to depend on PostgreSQL and nothing
// else. Above the branch, a key-less production Job would refuse to start.
// ---------------------------------------------------------------
var jwtOptions = JwtOptions.FromConfiguration(builder.Configuration, builder.Environment.IsProduction());
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<ITokenIssuer>(new JwtTokenIssuer(jwtOptions));
builder.Services.AddSingleton<IPasswordHasher>(new Pbkdf2PasswordHasher());
builder.Services.AddScoped<IUserStore, UserStore>();
builder.Services.AddScoped<AuthService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
        };
    });

builder.Services.AddAuthorizationBuilder().AddFlashSalePolicies();

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

// Outermost user middleware: it must see every request — including ones
// rejected by authentication — so the in-flight gauge and the duration
// histogram describe the whole surface, not just the authorized part.
app.UseFlashSaleMetrics();

// Order matters: authentication must populate HttpContext.User before
// authorization evaluates the policies registered above.
app.UseAuthentication();
app.UseAuthorization();

// Startup concerns live in Infrastructure (schema + seed + Redis mirroring).
await DatabaseInitializer.InitializeAsync(app.Services);

app.MapAuthEndpoints();

// OpenAPI document (Phase III). Served as JSON at /openapi/v1.json, which is a
// stable, tool-agnostic contract: any client that can read a URL can generate a
// typed client or render docs from it.
app.MapOpenApi();

// Metrics snapshot (Phase III). Deliberately a JSON snapshot rather than a
// Prometheus exporter: the interview surface needs a dependency-free view of
// what THIS process observed, and the instruments behind it are real Meter
// instruments, so an OTel exporter can be attached later without a rewrite.
app.MapGet("/internal/metrics", (ApiMetrics metrics) => Results.Ok(metrics.Snapshot()))
    .WithTags("Ops")
    .WithName("MetricsSnapshot")
    .WithSummary("In-process metrics snapshot as JSON (counters + duration histograms).");


app.MapPost("/api/orders", async (
    OrderRequest request,
    HttpRequest http,
    ClaimsPrincipal user,
    IOrderReadModel readModel,
    IStockReservationGateway redis,
    IOrderQueueProducer queue,
    OrderProcessor processor,
    ILogger<Program> logger) =>
{
    if (request.Quantity <= 0)
    {
        ApiMetrics.OrderRejected("invalid_quantity");
        return Results.BadRequest(new { error = "Quantity must be positive" });
    }

    var idempotencyKey = http.Headers.TryGetValue("Idempotency-Key", out var headerKey)
        ? headerKey.ToString()
        : Guid.NewGuid().ToString("N");

    // ADR-013 §6: anonymous callers keep working (UserId = null); an
    // authenticated caller's order is attributed to the JWT `sub`. This endpoint
    // is deliberately NOT [Authorize] — the anonymous path is existing,
    // verified behaviour and the concurrency evidence depends on it.
    var userId = TryGetUserId(user);

    var message = new OrderMessage(request.ProductId, request.Quantity, idempotencyKey, DateTimeOffset.UtcNow, UserId: userId);

    // T1 — fast reservation (also deduplicates by idempotency key).
    var reservation = await redis.TryReserveAsync(request.ProductId, request.Quantity, idempotencyKey);
    if (reservation == ReservationResult.Duplicate)
    {
        ApiMetrics.OrderRejected("duplicate");
        return Results.Json(new { error = "Duplicate request", idempotencyKey }, statusCode: 409);
    }

    if (reservation == ReservationResult.SoldOut)
    {
        // Refused by the Redis pre-filter — the fast path doing its job.
        ApiMetrics.OrderRejected("out_of_stock");
        return Results.Json(new { error = "Out of stock" }, statusCode: 409);
    }

    // T1b — unknown product: seed the reservation tier from the DB once, then retry.
    if (reservation == ReservationResult.UnknownProduct)
    {
        var stock = await readModel.GetStockAsync(request.ProductId);
        if (stock is null)
        {
            ApiMetrics.OrderRejected("unknown_product");
            return Results.NotFound(new { error = "Product not found" });
        }

        await redis.SetStockAsync(request.ProductId, stock.Value);
        reservation = await redis.TryReserveAsync(request.ProductId, request.Quantity, idempotencyKey);
        if (reservation == ReservationResult.SoldOut)
        {
            ApiMetrics.OrderRejected("out_of_stock");
            return Results.Json(new { error = "Out of stock" }, statusCode: 409);
        }
    }

    if (reservation == ReservationResult.Reserved)
    {
        // T2 — hand off to the fulfillment pipeline.
        if (await queue.EnqueueAsync(message))
        {
            ApiMetrics.OrdersAccepted.Add(1);
            return Results.Accepted(
                $"/api/orders/{idempotencyKey}",
                new { message = "Order accepted", idempotencyKey, status = "processing" });
        }

        // Queue full -> backpressure: give the reservation back and shed load.
        await redis.ReleaseReservationAsync(request.ProductId, request.Quantity, idempotencyKey);
        ApiMetrics.OrderRejected("backpressure");
        return Results.Json(new { error = "System busy, retry shortly" }, statusCode: 503);
    }

    // T1c — reservation tier unavailable: fall back to the synchronous use case
    // (Phase 3 path; still idempotent thanks to the repository constraint).
    logger.LogWarning("Reservation tier unavailable ({Reservation}) — falling back to the synchronous path.", reservation);
    try
    {
        await processor.ProcessAsync(message, CancellationToken.None);
        ApiMetrics.OrdersAccepted.Add(1);
        // Scope note: this counter means "completed by THIS process". Orders that
        // went through the queue are persisted by Order.Worker, which is a separate
        // process with its own meter instance — counting them here would be a guess.
        ApiMetrics.OrdersCompleted.Add(1);
        return Results.Ok(new { Message = "Order placed successfully", IdempotencyKey = idempotencyKey });
    }
    catch (StockDriftException)
    {
        // ADR-002's guard fired: the conditional UPDATE matched zero rows and the
        // order was refused instead of overselling. Counted separately from a Redis
        // refusal because drift means the two tiers disagreed — worth an alert,
        // whereas a Redis "sold out" is the expected outcome of a flash sale.
        ApiMetrics.StockDrift.Add(1);
        ApiMetrics.OrderRejected("stock_drift");
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

// The caller's own order history (ADR-013 §5). Scoped by the JWT `sub`, so a
// caller can only ever see their own orders — no resource-based handler is
// needed because the query itself is the authorization boundary.
app.MapGet("/orders/me", async (ClaimsPrincipal user, IOrderReadModel readModel, CancellationToken ct) =>
{
    var userId = TryGetUserId(user);
    if (userId is null) return Results.Unauthorized();

    var orders = await readModel.GetOrdersByUserAsync(userId.Value, ct);
    return Results.Ok(new { count = orders.Count, orders });
})
.RequireAuthorization()
.WithTags("Orders")
.WithName("MyOrders")
.WithSummary("List the authenticated caller's orders, newest first.");

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

/// <summary>
/// Read the caller's user id from the JWT <c>sub</c> claim, or <c>null</c> when
/// the request is anonymous (ADR-013 §6).
/// </summary>
/// <remarks>
/// A local function, not a helper class: it is used by exactly two endpoints in
/// this file, and both need the same "anonymous is allowed" answer.
/// </remarks>
static Guid? TryGetUserId(ClaimsPrincipal user)
{
    var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    return Guid.TryParse(sub, out var id) ? id : null;
}

app.Run();

// Explicit, because the --migrate branch above returns an int: top-level
// statements require every code path to return a value once one of them does.
return 0;

public record OrderRequest(int ProductId, int Quantity);
