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

// Redis: enabled when reachable, transparently skipped otherwise.
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddSingleton<IStockReservationGateway>(sp =>
    new RedisStockGateway(sp.GetRequiredService<IConnectionMultiplexer>()));

// Queue selection: RabbitMQ when configured, in-memory otherwise.
var rabbitMqConnectionString = builder.Configuration.GetConnectionString("RabbitMQ");
if (!string.IsNullOrEmpty(rabbitMqConnectionString))
{
    builder.Services.AddSingleton<RabbitMQOrderQueue>(sp =>
        new RabbitMQOrderQueue(rabbitMqConnectionString, "orders",
            sp.GetRequiredService<ILogger<RabbitMQOrderQueue>>()));
    builder.Services.AddSingleton<IOrderQueueProducer>(sp => sp.GetRequiredService<RabbitMQOrderQueue>());
    builder.Services.AddSingleton<IOrderQueueConsumer>(sp => sp.GetRequiredService<RabbitMQOrderQueue>());
}
else
{
    // One shared instance: producer and consumer MUST be the same channel.
    builder.Services.AddSingleton<InMemoryOrderQueue>();
    builder.Services.AddSingleton<IOrderQueueProducer>(sp => sp.GetRequiredService<InMemoryOrderQueue>());
    builder.Services.AddSingleton<IOrderQueueConsumer>(sp => sp.GetRequiredService<InMemoryOrderQueue>());
}

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

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.Run();

public record OrderRequest(int ProductId, int Quantity);
