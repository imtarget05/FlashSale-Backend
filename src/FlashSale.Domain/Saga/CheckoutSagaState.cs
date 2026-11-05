namespace FlashSale.Domain.Saga;

public enum SagaStatus
{
    Pending,
    Completed,
    Failed,
    Compensated
}

public enum InventoryReservationStatus
{
    Rejected,
    Reserved,
    Released
}

public enum PaymentTransactionStatus
{
    Pending,
    Successful,
    Declined,
    TimedOut
}

public sealed class CheckoutSagaState
{
    public Guid SagaId { get; set; } = Guid.NewGuid();
    public string IdempotencyKey { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public SagaStatus Status { get; set; } = SagaStatus.Pending;
    public InventoryReservationStatus InventoryStatus { get; set; } = InventoryReservationStatus.Rejected;
    public PaymentTransactionStatus PaymentStatus { get; set; } = PaymentTransactionStatus.Pending;
    public int OrderId { get; set; }
    public string? FailureReason { get; set; }
    public string? CompensationReason { get; set; }
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
