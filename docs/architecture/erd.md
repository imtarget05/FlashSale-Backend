# Entity Relationship Model (OpsDomain)

> Grounded in `src/OpsBridge.Domain/*` and `src/OpsBridge.Infrastructure/Persistence/OpsBridgeDbContext.cs`.

## Các entity chính

| Entity | Vai trò | Thuộc tính quan trọng |
|---|---|---|
| `PurchaseOrder` | Đơn mua (PO) | Id, OrderNumber, SupplierCode, Currency, Status, TotalAmount, OrderedAt |
| `PurchaseOrderLine` | Dòng PO | Id, ItemSku, OrderedQuantity, UnitOfMeasure, UnitPrice, TotalAmount |
| `GoodsReceipt` | Phiếu nhập kho (GR) | Id, ReceiptNumber, PurchaseOrderNumber, ReceivedAt, SourceSystem |
| `GoodsReceiptLine` | Dòng GR | Id, ItemSku, ReceivedQuantity, AllocatedQuantity, AvailableQuantity |
| `SupplierInvoice` | Hóa đơn NCC | Id, InvoiceNumber, PurchaseOrderNumber, Currency, Status, SubTotal, TaxAmount, TotalAmount |
| `SupplierInvoiceLine` | Dòng hóa đơn | Id, ItemSku, InvoicedQuantity, UnitOfMeasure, UnitPrice, TaxRate |
| `MappingRule` | Quy tắc映射 SKU/UOM | Id, RuleType (SkuAlias, UomConversion), SourceValue, TargetValue, ConversionFactor, SupplierCode, IsActive |
| `ReconciliationResult` | Kết quả đối soát | Id, PurchaseOrderNumber, InvoiceNumber, Status (Matched/Discrepancy), Totals, CreatedAt |
| `ReconciliationAllocation` | Phân bổ hàng | Id, GoodsReceiptLineId, InvoiceLineId, AllocatedQuantity, UnitPrice |
| `Discrepancy` | Sai lệch đối soát | Id, Type (MissingPO, CurrencyMismatch, SupplierMismatch, QuantityMismatch...), Severity, Status (UnderReview/Resolved/Rejected) |
| `ReviewDecision` | Quyết định phê duyệt | Id, Action (ApprovedWithTolerance/Adjusted/Rejected), ReviewerUserId, ReviewerRole, Notes, DecidedAt |
| `AuditEvent` | Nhật ký thao tác | Id, EntityType, EntityId, Action, UserId, Timestamp, Details |
| `OutboxMessage` | Tin nhắn outbox | Id, EventType, Topic, Payload, Status (Pending/Processed), CreatedAt, ProcessedAt |
| `InboxMessage` | Tin nhắn inbox (dedupe) | Id, EventId, Payload, ProcessedAt |
| `ConsumerProcessingState` | Trạng thái xử lý consumer | EventId, Payload hash, processed flag |
| `DeadLetterEvent` | Sự kiện vào DLQ | Id, EventId, EventType, Topic, Payload, FailedAt, Reason, RetryCount, IsReplayed, ReplayedAt, ReplayedBy |
| `ConnectorConfig` | Cấu hình kết nối bên ngoài | Id, Name, Type, Secret, BaseUrl, IsActive |

## Quan hệ chính (1:N)

```
PurchaseOrder 1:N PurchaseOrderLine
PurchaseOrder 1:N GoodsReceipt
SupplierInvoice 1:N SupplierInvoiceLine
GoodsReceipt 1:N GoodsReceiptLine
ReconciliationResult 1:N ReconciliationAllocation
ReconciliationResult 1:N Discrepancy
Discrepancy 1 ReviewDecision
```

## Bất biến dữ liệu (invariant)

- **INV-ALLOC:** Tổng `AllocatedQuantity` trên các `GoodsReceiptLine` không vượt quá `ReceivedQuantity` (conditional update `TryAllocateQuantityAsync` tại row level).
- **INV-IDEM:** Cùng `EventId` trong `InboxMessage` chỉ được xử lý 1 lần (unique index, PostgreSQL 23505).
- **INV-REPLAY:** Cùng `DeadLetterEvent.EventId` chỉ được đánh dấu `IsReplayed = true` 1 lần (conditional `WHERE ... AND !IsReplayed`, `ExecuteUpdateAsync` trả về số dòng cập nhật).
- **INV-TXN:** Outbox + nghiệp vụ commit trong 1 transaction PostgreSQL qua `EfUnitOfWork`.
