namespace FlashSale.Application.Payment;

public sealed record PaymentClientRequest(
    Guid PaymentId,
    int OrderId,
    string IdempotencyKey,
    decimal Amount,
    string Currency = "USD");

public sealed record PaymentClientResult(
    bool Success,
    string Status,
    string? TransactionId,
    string? FailureReason,
    bool IsTransientError);

/// <summary>
/// Port representing the payment bounded context client (Phase 9 & 11).
/// Communicates with Payment.Service or simulated payment adapter.
/// </summary>
public interface IPaymentClient
{
    Task<PaymentClientResult> ProcessPaymentAsync(PaymentClientRequest request, CancellationToken ct = default);
}
