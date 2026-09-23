using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

var paymentStore = new ConcurrentDictionary<string, PaymentRecordDto>();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Payment.Service", timestamp = DateTimeOffset.UtcNow }));

app.MapPost("/api/payments", (ProcessPaymentRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
    {
        return Results.BadRequest(new { error = "IdempotencyKey is required." });
    }

    // Idempotent replay: return identical result if request was already processed
    if (paymentStore.TryGetValue(request.IdempotencyKey, out var existing))
    {
        return Results.Ok(existing);
    }

    // Deterministic simulation rule:
    // Cent value .01 -> success
    // Cent value .02 -> declined (insufficient funds)
    // Cent value .03 -> transient gateway timeout
    var cents = (int)Math.Round((request.Amount - Math.Floor(request.Amount)) * 100);

    PaymentRecordDto result;
    if (cents == 2)
    {
        result = new PaymentRecordDto(
            Success: false,
            Status: "Declined",
            TransactionId: null,
            FailureReason: "Insufficient funds in customer account",
            IsTransientError: false,
            ProcessedAt: DateTimeOffset.UtcNow);

        paymentStore[request.IdempotencyKey] = result;
        return Results.Ok(result);
    }

    if (cents == 3)
    {
        result = new PaymentRecordDto(
            Success: false,
            Status: "TimedOut",
            TransactionId: null,
            FailureReason: "Upstream payment gateway timeout",
            IsTransientError: true,
            ProcessedAt: DateTimeOffset.UtcNow);

        paymentStore[request.IdempotencyKey] = result;
        return Results.Json(result, statusCode: StatusCodes.Status504GatewayTimeout);
    }

    // Default / .01: Successful payment
    result = new PaymentRecordDto(
        Success: true,
        Status: "Succeeded",
        TransactionId: $"tx_{Guid.NewGuid():N}",
        FailureReason: null,
        IsTransientError: false,
        ProcessedAt: DateTimeOffset.UtcNow);

    paymentStore[request.IdempotencyKey] = result;
    return Results.Ok(result);
});

app.MapGet("/api/payments/{idempotencyKey}", (string idempotencyKey) =>
{
    if (paymentStore.TryGetValue(idempotencyKey, out var record))
    {
        return Results.Ok(record);
    }
    return Results.NotFound(new { error = $"Payment for key '{idempotencyKey}' not found." });
});

app.Run();

public record ProcessPaymentRequest(
    Guid PaymentId,
    int OrderId,
    string IdempotencyKey,
    decimal Amount,
    string Currency);

public record PaymentRecordDto(
    bool Success,
    string Status,
    string? TransactionId,
    string? FailureReason,
    bool IsTransientError,
    DateTimeOffset ProcessedAt);
