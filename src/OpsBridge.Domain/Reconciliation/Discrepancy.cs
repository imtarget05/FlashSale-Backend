namespace OpsBridge.Domain.Reconciliation;

public enum DiscrepancyType
{
    QuantityMismatch,
    PriceMismatch,
    MissingPurchaseOrder,
    MissingGoodsReceipt,
    UnmatchedInvoiceLine,
    DuplicateInvoice,
    CurrencyMismatch,
    UomConversionError
}

public enum DiscrepancySeverity
{
    Warning,
    Error
}

public enum DiscrepancyStatus
{
    Open,
    UnderReview,
    Resolved,
    Rejected
}

public sealed class Discrepancy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReconciliationResultId { get; set; }
    public DiscrepancyType Type { get; set; }
    public DiscrepancySeverity Severity { get; set; } = DiscrepancySeverity.Error;
    public string ItemSku { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
    public string ActualValue { get; set; } = string.Empty;
    public decimal VarianceAmount { get; set; }
    public DiscrepancyStatus Status { get; set; } = DiscrepancyStatus.Open;
    public string Details { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ReviewDecision? ReviewDecision { get; set; }
}

public enum ReviewAction
{
    ApprovedWithTolerance,
    Rejected,
    Adjusted
}

public sealed class ReviewDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DiscrepancyId { get; set; }
    public ReviewAction Action { get; set; }
    public string ReviewerUserId { get; set; } = string.Empty;
    public string ReviewerRole { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string UserId { get; set; } = "System";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Details { get; set; } = string.Empty;
}
