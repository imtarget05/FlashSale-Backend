namespace FlashSale.Domain.Saga;

/// <summary>High-level lifecycle status of the Checkout Distributed Saga.</summary>
public enum SagaStatus
{
    Started,
    InventoryReserved,
    PaymentProcessing,
    Completed,
    Compensating,
    Compensated,
    Failed
}

/// <summary>Inventory reservation status within the Saga boundary.</summary>
public enum InventoryReservationStatus
{
    Pending,
    Reserved,
    Released,
    Rejected
}

/// <summary>Payment transaction status within the Saga boundary.</summary>
public enum PaymentTransactionStatus
{
    Pending,
    Successful,
    Declined,
    TimedOut,
    Failed
}

/// <summary>
/// Durable entity tracking the state and progression of a Checkout Distributed Saga (Phases 9 & 11).
/// Guarantees eventual consistency across Inventory and Payment bounded contexts via forward steps
/// and compensating transactions.
/// </summary>
public class CheckoutSagaState
{
    public Guid SagaId { get; set; } = Guid.NewGuid();
    public int OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public Guid? UserId { get; set; }

    public SagaStatus Status { get; set; } = SagaStatus.Started;
    public InventoryReservationStatus InventoryStatus { get; set; } = InventoryReservationStatus.Pending;
    public PaymentTransactionStatus PaymentStatus { get; set; } = PaymentTransactionStatus.Pending;

    public string? FailureReason { get; set; }
    public string? CompensationReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency guard.</summary>
    public int Version { get; set; } = 1;
}
