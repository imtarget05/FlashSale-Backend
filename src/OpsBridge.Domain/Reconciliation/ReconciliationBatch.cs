namespace OpsBridge.Domain.Reconciliation;

public enum BatchStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public enum MatchStatus
{
    Matched,
    Discrepancy
}

public sealed class ReconciliationBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BatchNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExecutedAt { get; set; }
    public BatchStatus Status { get; set; } = BatchStatus.Pending;
    public string InitiatedBy { get; set; } = "System";

    public List<ReconciliationResult> Results { get; set; } = [];
}

public sealed class ReconciliationResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? BatchId { get; set; }
    public string PurchaseOrderNumber { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public MatchStatus Status { get; set; } = MatchStatus.Matched;
    public decimal TotalQuantityOrdered { get; set; }
    public decimal TotalQuantityReceived { get; set; }
    public decimal TotalQuantityInvoiced { get; set; }
    public decimal TotalAmountVariance { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ReconciliationAllocation> Allocations { get; set; } = [];
    public List<Discrepancy> Discrepancies { get; set; } = [];
}

public sealed class ReconciliationAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReconciliationResultId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public Guid GoodsReceiptLineId { get; set; }
    public string ItemSku { get; set; } = string.Empty;
    public decimal AllocatedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
