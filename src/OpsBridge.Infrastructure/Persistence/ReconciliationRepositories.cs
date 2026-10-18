using Microsoft.EntityFrameworkCore;
using OpsBridge.Application.Reconciliation;
using OpsBridge.Domain.Reconciliation;

namespace OpsBridge.Infrastructure.Persistence;

public sealed class EfPurchaseOrderRepository(OpsBridgeDbContext db) : IPurchaseOrderRepository
{
    public async Task<PurchaseOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default)
    {
        return await db.PurchaseOrders
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.OrderNumber == orderNumber, ct);
    }

    public async Task AddAsync(PurchaseOrder po, CancellationToken ct = default)
    {
        await db.PurchaseOrders.AddAsync(po, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PurchaseOrder po, CancellationToken ct = default)
    {
        db.PurchaseOrders.Update(po);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PurchaseOrder>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        return await db.PurchaseOrders
            .Include(p => p.Lines)
            .OrderByDescending(p => p.OrderedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}

public sealed class EfGoodsReceiptRepository(OpsBridgeDbContext db) : IGoodsReceiptRepository
{
    public async Task<GoodsReceipt?> GetByReceiptNumberAsync(string receiptNumber, CancellationToken ct = default)
    {
        return await db.GoodsReceipts
            .Include(g => g.Lines)
            .FirstOrDefaultAsync(g => g.ReceiptNumber == receiptNumber, ct);
    }

    public async Task<IReadOnlyList<GoodsReceipt>> GetByPurchaseOrderNumberAsync(string orderNumber, CancellationToken ct = default)
    {
        return await db.GoodsReceipts
            .Include(g => g.Lines)
            .Where(g => g.PurchaseOrderNumber == orderNumber)
            .OrderBy(g => g.ReceivedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(GoodsReceipt gr, CancellationToken ct = default)
    {
        await db.GoodsReceipts.AddAsync(gr, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateLineAsync(GoodsReceiptLine line, CancellationToken ct = default)
    {
        db.GoodsReceiptLines.Update(line);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryAllocateQuantityAsync(Guid lineId, decimal quantityToAllocate, CancellationToken ct = default)
    {
        var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE ""GoodsReceiptLines""
               SET ""AllocatedQuantity"" = ""AllocatedQuantity"" + {quantityToAllocate}
               WHERE ""Id"" = {lineId}
                 AND (""AllocatedQuantity"" + {quantityToAllocate}) <= ""ReceivedQuantity""", ct);

        return rowsAffected > 0;
    }

    public async Task<bool> ReleaseAllocationQuantityAsync(Guid lineId, decimal quantityToRelease, CancellationToken ct = default)
    {
        var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE ""GoodsReceiptLines""
               SET ""AllocatedQuantity"" = GREATEST(""AllocatedQuantity"" - {quantityToRelease}, 0)
               WHERE ""Id"" = {lineId}", ct);

        return rowsAffected > 0;
    }

    public async Task<IReadOnlyList<GoodsReceipt>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        return await db.GoodsReceipts
            .Include(g => g.Lines)
            .OrderByDescending(g => g.ReceivedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}

public sealed class EfSupplierInvoiceRepository(OpsBridgeDbContext db) : ISupplierInvoiceRepository
{
    public async Task<SupplierInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await db.SupplierInvoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<SupplierInvoice?> GetByNumberAndSupplierAsync(string invoiceNumber, string supplierCode, CancellationToken ct = default)
    {
        return await db.SupplierInvoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber && i.SupplierCode == supplierCode, ct);
    }

    public async Task<IReadOnlyList<SupplierInvoice>> GetByPurchaseOrderNumberAsync(string poNumber, CancellationToken ct = default)
    {
        return await db.SupplierInvoices
            .Include(i => i.Lines)
            .Where(i => i.PurchaseOrderNumber == poNumber)
            .OrderBy(i => i.InvoiceDate)
            .ToListAsync(ct);
    }

    public async Task AddAsync(SupplierInvoice invoice, CancellationToken ct = default)
    {
        await db.SupplierInvoices.AddAsync(invoice, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SupplierInvoice invoice, CancellationToken ct = default)
    {
        db.SupplierInvoices.Update(invoice);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SupplierInvoice>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        return await db.SupplierInvoices
            .Include(i => i.Lines)
            .OrderByDescending(i => i.InvoiceDate)
            .Take(limit)
            .ToListAsync(ct);
    }
}

public sealed class EfMappingRuleRepository(OpsBridgeDbContext db) : IMappingRuleRepository
{
    public async Task<IReadOnlyList<MappingRule>> GetActiveRulesAsync(CancellationToken ct = default)
    {
        return await db.MappingRules
            .Where(r => r.IsActive)
            .ToListAsync(ct);
    }

    public async Task AddAsync(MappingRule rule, CancellationToken ct = default)
    {
        await db.MappingRules.AddAsync(rule, ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class EfReconciliationRepository(OpsBridgeDbContext db) : IReconciliationRepository
{
    public async Task AddBatchAsync(ReconciliationBatch batch, CancellationToken ct = default)
    {
        await db.ReconciliationBatches.AddAsync(batch, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReconciliationBatch?> GetBatchByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await db.ReconciliationBatches
            .Include(b => b.Results)
                .ThenInclude(r => r.Discrepancies)
            .Include(b => b.Results)
                .ThenInclude(r => r.Allocations)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task AddResultAsync(ReconciliationResult result, CancellationToken ct = default)
    {
        await db.ReconciliationResults.AddAsync(result, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReconciliationResult?> GetResultByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await db.ReconciliationResults
            .Include(r => r.Allocations)
            .Include(r => r.Discrepancies)
                .ThenInclude(d => d.ReviewDecision)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<ReconciliationResult>> GetResultsByInvoiceNumberAsync(string invoiceNumber, CancellationToken ct = default)
    {
        return await db.ReconciliationResults
            .Include(r => r.Allocations)
            .Include(r => r.Discrepancies)
                .ThenInclude(d => d.ReviewDecision)
            .Where(r => r.InvoiceNumber == invoiceNumber)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReconciliationBatch>> ListBatchesAsync(int limit = 50, CancellationToken ct = default)
    {
        return await db.ReconciliationBatches
            .Include(b => b.Results)
            .OrderByDescending(b => b.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<DashboardMetricsDto> GetMetricsAsync(CancellationToken ct = default)
    {
        var totalBatches = await db.ReconciliationBatches.CountAsync(ct);
        var totalInvoices = await db.SupplierInvoices.CountAsync(ct);
        var matchedInvoices = await db.SupplierInvoices.CountAsync(i => i.Status == InvoiceStatus.Matched || i.Status == InvoiceStatus.ApprovedWithVariance, ct);
        var discrepantInvoices = await db.SupplierInvoices.CountAsync(i => i.Status == InvoiceStatus.Discrepancy, ct);
        var openDiscrepancies = await db.Discrepancies.CountAsync(d => d.Status == DiscrepancyStatus.Open || d.Status == DiscrepancyStatus.UnderReview, ct);
        var activeConnectors = await db.Connectors.CountAsync(c => c.IsActive, ct);
        var deadLetterCount = await db.DeadLetterEvents.CountAsync(d => !d.IsReplayed, ct);

        return new DashboardMetricsDto(
            totalBatches,
            totalInvoices,
            matchedInvoices,
            discrepantInvoices,
            openDiscrepancies,
            activeConnectors,
            deadLetterCount
        );
    }

    public async Task<IReadOnlyList<Discrepancy>> GetDiscrepanciesAsync(DiscrepancyStatus? status = null, CancellationToken ct = default)
    {
        var query = db.Discrepancies
            .Include(d => d.ReviewDecision)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        return await query.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
    }

    public async Task<Discrepancy?> GetDiscrepancyByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await db.Discrepancies
            .Include(d => d.ReviewDecision)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task UpdateDiscrepancyAsync(Discrepancy discrepancy, CancellationToken ct = default)
    {
        db.Discrepancies.Update(discrepancy);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddReviewDecisionAsync(ReviewDecision decision, CancellationToken ct = default)
    {
        await db.ReviewDecisions.AddAsync(decision, ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class EfAuditEventRepository(OpsBridgeDbContext db) : IAuditEventRepository
{
    public async Task AddAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        await db.AuditEvents.AddAsync(auditEvent, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        return await db.AuditEvents
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .ToListAsync(ct);
    }
}
