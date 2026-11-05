namespace FlashSale.Application.Payment;

public sealed record PaymentClientRequest(
    string IdempotencyKey,
    int OrderId,
    decimal Amount,
    string Currency);

public sealed record PaymentClientResult(
    bool Success,
    string Status,
    string? TransactionId,
    string? FailureReason,
    bool IsTransientError);

public interface IPaymentClient
{
    Task<PaymentClientResult> ProcessPaymentAsync(PaymentClientRequest request, CancellationToken ct = default);
}
