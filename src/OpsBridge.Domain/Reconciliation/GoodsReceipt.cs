namespace OpsBridge.Domain.Reconciliation;

public sealed class GoodsReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ReceiptNumber { get; set; } = string.Empty;
    public string PurchaseOrderNumber { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? SourceEventId { get; set; }
    public string SourceSystem { get; set; } = "FactoryFlow";
    public int Version { get; set; } = 1;

    public List<GoodsReceiptLine> Lines { get; set; } = [];
}

public sealed class GoodsReceiptLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GoodsReceiptId { get; set; }
    public int LineNumber { get; set; }
    public string ItemSku { get; set; } = string.Empty;
    public decimal ReceivedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; } = 0m;
    public string UnitOfMeasure { get; set; } = "PCS";

    public decimal AvailableQuantity => ReceivedQuantity - AllocatedQuantity;
}
