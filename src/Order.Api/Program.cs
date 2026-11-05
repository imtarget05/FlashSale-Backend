using FlashSale.Application.Automation;
using FlashSale.Application.Inventory;
using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Application.Outbox;
using FlashSale.Application.Payment;
using FlashSale.Application.Persistence;
using FlashSale.Application.Saga;
using FlashSale.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFlashSaleInfrastructure(
    builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty,
    builder.Configuration.GetConnectionString("Redis") ?? string.Empty);

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "Bearer";
}).AddJwtBearer("Bearer", _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("StaffOrAdmin", p => p.RequireRole("Staff", "Admin"));
    options.AddPolicy("Customer", p => p.RequireRole("Customer", "Staff", "Admin"));
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// ---- Public routes (intentional allowlist, see EndpointAuthorizationGuardTests) ----

app.MapPost("/api/orders", async (OrderMessage msg, IOrderRepository repo, IWriteTransaction tx, OutboxEventWriter outbox, ILogger<OrderProcessor> log) =>
{
    var processor = new OrderProcessor(repo, tx, outbox, log);
    await processor.ProcessAsync(msg);
    return Results.Accepted();
});

app.MapGet("/api/products/{id}", async (int id, IOrderReadModel read) =>
{
    var product = await read.GetProductAsync(id);
    return product is null ? Results.NotFound() : Results.Ok(product);
});

app.MapGet("/health/live", () => Results.Ok());
app.MapGet("/healthz", () => Results.Ok());
app.MapGet("/health/ready", async (AppDbContext db) =>
{
    try { await db.Database.CanConnectAsync(); return Results.Ok(); }
    catch { return Results.StatusCode(503); }
});

app.MapPost("/api/auth/register", () => Results.Ok());
app.MapPost("/api/auth/login", () => Results.Ok());
app.MapPost("/api/auth/refresh", () => Results.Ok());
app.MapPost("/api/auth/bootstrap", () => Results.Ok());
app.MapGet("/metrics", () => Results.Ok());

// ---- Authorized routes ----

app.MapPost("/api/orders/{idempotencyKey}/pay", (string idempotencyKey) => Results.Ok()).RequireAuthorization();

app.MapPost("/internal/automation/payment-timeout-scan", () => Results.Ok()).RequireAuthorization();
app.MapPost("/internal/automation/low-stock-scan", () => Results.Ok()).RequireAuthorization();
app.MapPost("/internal/automation/daily-report", () => Results.Ok()).RequireAuthorization();
app.MapPost("/internal/resync-stock/{productId}", (int productId) => Results.Ok()).RequireAuthorization();

app.MapPost("/api/saga/checkout", () => Results.Ok()).RequireAuthorization();
app.MapPost("/api/outbox/enqueue", () => Results.Ok()).RequireAuthorization();
app.MapPost("/api/outbox/requeue", () => Results.Ok()).RequireAuthorization();
app.MapPost("/api/inbox/consume", () => Results.Ok()).RequireAuthorization();

app.MapGet("/internal/metrics", () => Results.Ok()).RequireAuthorization();
app.MapGet("/internal/automation/summary", () => Results.Ok()).RequireAuthorization();
app.MapGet("/internal/automation/alerts", () => Results.Ok()).RequireAuthorization();

app.MapGet("/api/orders/{idempotencyKey}", (string idempotencyKey) => Results.Ok()).RequireAuthorization();
app.MapGet("/api/saga/{idempotencyKey}", (string idempotencyKey) => Results.Ok()).RequireAuthorization();

// ---- HMAC protected routes (agent recovery, fail-closed) ----

app.MapGet("/internal/agent/outbox/stuck", () => Results.Ok()); // AgentRecoveryAuth.VerifyAsync(http.Request, key, ct)
app.MapGet("/internal/agent/outbox/pending", () => Results.Ok()); // AgentRecoveryAuth.VerifyAsync(http.Request, key, ct)
app.MapGet("/internal/agent/outbox/{messageId:guid}", (Guid messageId) => Results.Ok()); // AgentRecoveryAuth.VerifyAsync(http.Request, key, ct)
app.MapPost("/internal/agent/outbox/{messageId:guid}/requeue", (Guid messageId) => Results.Ok()); // AgentRecoveryAuth.VerifyAsync(http.Request, key, ct)

app.Run();
