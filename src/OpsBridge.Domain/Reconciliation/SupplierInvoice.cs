namespace OpsBridge.Domain.Reconciliation;

public enum InvoiceStatus
{
    Draft,
    PendingReconciliation,
    PartiallyMatched,
    Matched,
    Discrepancy,
    Approved,
    ApprovedWithVariance,
    Rejected,
    Cancelled
}

public sealed class SupplierInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InvoiceNumber { get; set; } = string.Empty;
    public string PurchaseOrderNumber { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateTimeOffset InvoiceDate { get; set; } = DateTimeOffset.UtcNow;
    public string Currency { get; set; } = "VND";
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; } = 0m;
    public decimal DiscountAmount { get; set; } = 0m;
    public decimal AdditionalCharges { get; set; } = 0m;
    public decimal TotalAmount { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.PendingReconciliation;
    public string SourceSystem { get; set; } = "ManualImport";
    public int Version { get; set; } = 1;

    public List<SupplierInvoiceLine> Lines { get; set; } = [];
}

public sealed class SupplierInvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SupplierInvoiceId { get; set; }
    public int LineNumber { get; set; }
    public string ItemSku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal InvoicedQuantity { get; set; }
    public string UnitOfMeasure { get; set; } = "PCS";
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; } = 0m;
    public decimal LineTotal { get; set; }
    public decimal MatchedQuantity { get; set; } = 0m;
}
