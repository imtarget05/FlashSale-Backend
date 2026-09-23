using FlashSale.Domain;

namespace FlashSale.Application.Persistence;

/// <summary>One row the payment automation acts on (spec §4/§5).</summary>
public sealed record PendingPaymentView(
    int OrderId,
    int ProductId,
    int Quantity,
    string IdempotencyKey,
    DateTime CreatedAt,
    DateTimeOffset? PaymentDueAt,
    int PaymentReminderCount);

/// <summary>Order lookup for the payment-recording endpoint (spec §4 paid branch).</summary>
public sealed record PaymentOrderView(
    int OrderId,
    int ProductId,
    int Quantity,
    string IdempotencyKey,
    OrderStatus Status);

/// <summary>
/// Port: payment/stock lifecycle transitions with STATUS GUARDS, so every
/// transition is exactly-once even under at-least-once triggers (spec §4, §12).
/// </summary>
public interface IPaymentRepository
{
    /// <summary>
    /// PendingPayment → Cancelled AND stock incremented in ONE transaction.
    /// Returns false when the row is no longer pending (already cancelled/paid).
    /// </summary>
    Task<bool> CancelAndReleaseStockAsync(int orderId, CancellationToken ct = default);

    /// <summary>PendingPayment → Confirmed (guard: only from pending).</summary>
    Task<bool> MarkPaidAsync(int orderId, CancellationToken ct = default);

    /// <summary>Record a failed payment attempt; order STAYS PendingPayment.</summary>
    Task<bool> MarkPaymentFailedAsync(int orderId, CancellationToken ct = default);

    /// <summary>Reminder counter bump (guard: only while pending).</summary>
    Task<int> IncrementReminderAsync(int orderId, CancellationToken ct = default);

    Task<PaymentOrderView?> GetByKeyAsync(string idempotencyKey, CancellationToken ct = default);
}