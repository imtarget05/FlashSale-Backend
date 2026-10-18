using OpsBridge.Domain.Reconciliation;

namespace OpsBridge.Application.Reconciliation;

public interface IPurchaseOrderRepository
{
    Task<PurchaseOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default);
    Task AddAsync(PurchaseOrder po, CancellationToken ct = default);
    Task UpdateAsync(PurchaseOrder po, CancellationToken ct = default);
    Task<IReadOnlyList<PurchaseOrder>> ListAsync(int limit = 50, CancellationToken ct = default);
}

public interface IGoodsReceiptRepository
{
    Task<GoodsReceipt?> GetByReceiptNumberAsync(string receiptNumber, CancellationToken ct = default);
    Task<IReadOnlyList<GoodsReceipt>> GetByPurchaseOrderNumberAsync(string orderNumber, CancellationToken ct = default);
    Task AddAsync(GoodsReceipt gr, CancellationToken ct = default);
    Task UpdateLineAsync(GoodsReceiptLine line, CancellationToken ct = default);
    Task<bool> TryAllocateQuantityAsync(Guid lineId, decimal quantityToAllocate, CancellationToken ct = default);
    Task<bool> ReleaseAllocationQuantityAsync(Guid lineId, decimal quantityToRelease, CancellationToken ct = default);
    Task<IReadOnlyList<GoodsReceipt>> ListAsync(int limit = 50, CancellationToken ct = default);
}

public interface ISupplierInvoiceRepository
{
    Task<SupplierInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SupplierInvoice?> GetByNumberAndSupplierAsync(string invoiceNumber, string supplierCode, CancellationToken ct = default);
    Task<IReadOnlyList<SupplierInvoice>> GetByPurchaseOrderNumberAsync(string poNumber, CancellationToken ct = default);
    Task AddAsync(SupplierInvoice invoice, CancellationToken ct = default);
    Task UpdateAsync(SupplierInvoice invoice, CancellationToken ct = default);
    Task<IReadOnlyList<SupplierInvoice>> ListAsync(int limit = 50, CancellationToken ct = default);
}

public interface IMappingRuleRepository
{
    Task<IReadOnlyList<MappingRule>> GetActiveRulesAsync(CancellationToken ct = default);
    Task AddAsync(MappingRule rule, CancellationToken ct = default);
}

public interface IReconciliationRepository
{
    Task AddBatchAsync(ReconciliationBatch batch, CancellationToken ct = default);
    Task<ReconciliationBatch?> GetBatchByIdAsync(Guid id, CancellationToken ct = default);
    Task AddResultAsync(ReconciliationResult result, CancellationToken ct = default);
    Task<ReconciliationResult?> GetResultByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationResult>> GetResultsByInvoiceNumberAsync(string invoiceNumber, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationBatch>> ListBatchesAsync(int limit = 50, CancellationToken ct = default);
    Task<DashboardMetricsDto> GetMetricsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Discrepancy>> GetDiscrepanciesAsync(DiscrepancyStatus? status = null, CancellationToken ct = default);
    Task<Discrepancy?> GetDiscrepancyByIdAsync(Guid id, CancellationToken ct = default);
    Task UpdateDiscrepancyAsync(Discrepancy discrepancy, CancellationToken ct = default);
    Task AddReviewDecisionAsync(ReviewDecision decision, CancellationToken ct = default);
}

public sealed record DashboardMetricsDto(
    int TotalBatches,
    int TotalInvoices,
    int MatchedInvoices,
    int DiscrepantInvoices,
    int OpenDiscrepancies,
    int ActiveConnectors,
    int DeadLetterCount
);

public interface IAuditEventRepository
{
    Task AddAsync(AuditEvent auditEvent, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int limit = 100, CancellationToken ct = default);
}
