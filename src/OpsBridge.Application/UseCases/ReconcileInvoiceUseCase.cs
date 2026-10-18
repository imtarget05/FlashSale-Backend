using Microsoft.Extensions.Logging;
using OpsBridge.Application.Common;
using OpsBridge.Application.Reconciliation;
using OpsBridge.Domain.Exceptions;
using OpsBridge.Domain.Reconciliation;

namespace OpsBridge.Application.UseCases;

public sealed class ReconcileInvoiceUseCase(
    IPurchaseOrderRepository poRepository,
    IGoodsReceiptRepository grRepository,
    ISupplierInvoiceRepository invoiceRepository,
    IMappingRuleRepository mappingRuleRepository,
    IReconciliationRepository reconciliationRepository,
    IAuditEventRepository auditRepository,
    IUnitOfWork unitOfWork,
    ILogger<ReconcileInvoiceUseCase> logger)
{
    private readonly ThreeWayMatchingEngine _engine = new();

    public async Task<ReconciliationResult> ExecuteAsync(
        Guid invoiceId,
        TolerancePolicy? tolerance = null,
        Guid? batchId = null,
        CancellationToken ct = default)
    {
        tolerance ??= new TolerancePolicy();

        var invoice = await invoiceRepository.GetByIdAsync(invoiceId, ct)
            ?? throw new DomainException($"Supplier invoice '{invoiceId}' was not found.");

        logger.LogInformation("Starting 3-way reconciliation for Invoice {InvoiceNumber} against PO {PoNumber}",
            invoice.InvoiceNumber, invoice.PurchaseOrderNumber);

        var po = await poRepository.GetByOrderNumberAsync(invoice.PurchaseOrderNumber, ct);
        var receipts = await grRepository.GetByPurchaseOrderNumberAsync(invoice.PurchaseOrderNumber, ct);
        var mappingRules = await mappingRuleRepository.GetActiveRulesAsync(ct);

        // Execute matching engine
        var result = _engine.Match(po, receipts, invoice, mappingRules, tolerance);
        result.BatchId = batchId;

        // Persist business effects in a single PostgreSQL transaction
        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            // 2. If matched, update allocated quantities on receipt lines with atomic conditional check
            if (result.Status == MatchStatus.Matched)
            {
                var allAllocationsSucceeded = true;
                foreach (var alloc in result.Allocations)
                {
                    var allocated = await grRepository.TryAllocateQuantityAsync(alloc.GoodsReceiptLineId, alloc.AllocatedQuantity, ct);
                    if (!allocated)
                    {
                        allAllocationsSucceeded = false;
                        logger.LogWarning("Concurrent allocation failed for receipt line {LineId}. Requested: {Qty}",
                            alloc.GoodsReceiptLineId, alloc.AllocatedQuantity);
                        break;
                    }
                }

                if (!allAllocationsSucceeded)
                {
                    // Concurrency race: Available stock was claimed by another transaction
                    result.Status = MatchStatus.Discrepancy;
                    result.Allocations.Clear();
                    result.Discrepancies.Add(new Discrepancy
                    {
                        Id = Guid.NewGuid(),
                        ReconciliationResultId = result.Id,
                        Type = DiscrepancyType.QuantityMismatch,
                        Severity = DiscrepancySeverity.Error,
                        ExpectedValue = "Available Stock",
                        ActualValue = "Claimed by concurrent allocation",
                        Details = "Receipt stock was claimed by a concurrent reconciliation transaction."
                    });
                    invoice.Status = InvoiceStatus.Discrepancy;
                }
            }

            // 1. Update Invoice status
            invoice.Status = result.Status == MatchStatus.Matched
                ? InvoiceStatus.Matched
                : InvoiceStatus.Discrepancy;
            await invoiceRepository.UpdateAsync(invoice, ct);

            // 3. Save reconciliation result, allocations, and discrepancies
            await reconciliationRepository.AddResultAsync(result, ct);

            // 4. Audit trail
            await auditRepository.AddAsync(new AuditEvent
            {
                EntityType = "SupplierInvoice",
                EntityId = invoice.Id.ToString(),
                Action = "ReconciliationExecuted",
                UserId = "System",
                Timestamp = DateTimeOffset.UtcNow,
                Details = $"Result: {result.Status}, Discrepancies: {result.Discrepancies.Count}, Allocations: {result.Allocations.Count}"
            }, ct);

            await tx.CommitAsync(ct);
            logger.LogInformation("Reconciliation completed for Invoice {InvoiceNumber}. Status: {Status}",
                invoice.InvoiceNumber, result.Status);

            return result;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            logger.LogError(ex, "Reconciliation transaction rolled back for Invoice {InvoiceNumber}", invoice.InvoiceNumber);
            throw;
        }
    }
}
