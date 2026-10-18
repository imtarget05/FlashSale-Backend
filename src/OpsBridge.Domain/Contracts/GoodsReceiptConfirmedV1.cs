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

public sealed record GoodsReceiptDataV1(
    Guid ReceiptId,
    string ReceiptNumber,
    string PurchaseOrderNumber,
    string SupplierCode,
    DateTimeOffset ReceivedDate,
    string WarehouseCode,
    string ReceivedBy,
    string? Notes,
    IReadOnlyList<GoodsReceiptItemV1> Items
);

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
