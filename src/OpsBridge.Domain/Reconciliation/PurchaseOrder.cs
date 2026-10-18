namespace OpsBridge.Domain.Reconciliation;

public enum PurchaseOrderStatus
{
    Draft,
    Open,
    PartiallyReceived,
    FullyReceived,
    Closed
}

public sealed class PurchaseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OrderNumber { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string Currency { get; set; } = "VND";
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Open;
    public DateTimeOffset OrderedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpectedDeliveryDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string SourceSystem { get; set; } = "FactoryFlow";
    public int Version { get; set; } = 1;

    public List<PurchaseOrderLine> Lines { get; set; } = [];
}

public sealed class PurchaseOrderLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseOrderId { get; set; }
    public int LineNumber { get; set; }
    public string ItemSku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public string UnitOfMeasure { get; set; } = "PCS";
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
}
