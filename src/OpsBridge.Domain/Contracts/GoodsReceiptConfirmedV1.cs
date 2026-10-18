namespace OpsBridge.Domain.Contracts;

public sealed record GoodsReceiptConfirmedV1(
    Guid EventId,
    string EventType,
    string SchemaVersion,
    string Source,
    DateTimeOffset OccurredAt,
    Guid CorrelationId,
    GoodsReceiptDataV1 Data
)
{
    public const string ExpectedEventType = "GoodsReceiptConfirmed";
    public const string ExpectedSchemaVersion = "1.0";
    public const string ExpectedSource = "factoryflow.manufacturing.warehouse";
}

public sealed class GoodsReceiptDataV1
{
    private string? _purchaseOrderNumber;
    private string? _purchaseOrderNo;

    public Guid ReceiptId { get; init; }
    public string ReceiptNumber { get; init; } = string.Empty;

    public string PurchaseOrderNumber
    {
        get => !string.IsNullOrWhiteSpace(_purchaseOrderNumber) ? _purchaseOrderNumber : (_purchaseOrderNo ?? string.Empty);
        init => _purchaseOrderNumber = value;
    }

    public string? PurchaseOrderNo
    {
        get => _purchaseOrderNo;
        init
        {
            _purchaseOrderNo = value;
            if (string.IsNullOrWhiteSpace(_purchaseOrderNumber))
            {
                _purchaseOrderNumber = value;
            }
        }
    }

    public string SupplierCode { get; init; } = string.Empty;
    public DateTimeOffset ReceivedDate { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public string ReceivedBy { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public IReadOnlyList<GoodsReceiptItemV1> Items { get; init; } = [];

    public GoodsReceiptDataV1() { }

    public GoodsReceiptDataV1(
        Guid receiptId,
        string receiptNumber,
        string purchaseOrderNumber,
        string supplierCode,
        DateTimeOffset receivedDate,
        string warehouseCode,
        string receivedBy,
        string? notes,
        IReadOnlyList<GoodsReceiptItemV1> items)
    {
        ReceiptId = receiptId;
        ReceiptNumber = receiptNumber;
        PurchaseOrderNumber = purchaseOrderNumber;
        SupplierCode = supplierCode;
        ReceivedDate = receivedDate;
        WarehouseCode = warehouseCode;
        ReceivedBy = receivedBy;
        Notes = notes;
        Items = items;
    }
}

public sealed record GoodsReceiptItemV1(
    string ItemCode,
    string ItemName,
    int PurchaseOrderLineNumber,
    decimal QuantityReceived,
    decimal QuantityRejected,
    string UnitOfMeasure,
    string LotNumber,
    string QcStatus
);
