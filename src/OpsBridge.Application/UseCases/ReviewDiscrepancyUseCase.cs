using Microsoft.Extensions.Logging;
using OpsBridge.Application.Common;
using OpsBridge.Application.Reconciliation;
using OpsBridge.Domain;
using OpsBridge.Domain.Exceptions;
using OpsBridge.Domain.Reconciliation;

namespace OpsBridge.Application.UseCases;

public sealed record ReviewDiscrepancyCommand(
    Guid DiscrepancyId,
    ReviewAction Action,
    string ReviewerUserId,
    string ReviewerRole,
    string Notes
);

public sealed class ReviewDiscrepancyUseCase(
    IReconciliationRepository reconciliationRepository,
    IAuditEventRepository auditRepository,
    ISupplierInvoiceRepository invoiceRepository,
    IGoodsReceiptRepository grRepository,
    IUnitOfWork unitOfWork,
    ILogger<ReviewDiscrepancyUseCase> logger)
{
    private static readonly HashSet<string> AllowedRoles =
    [
        OpsBridgeRoles.Admin,
        OpsBridgeRoles.Operator,
        OpsBridgeRoles.FinanceReviewer
    ];

    public async Task<ReviewDecision> ExecuteAsync(ReviewDiscrepancyCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.ReviewerRole) || !AllowedRoles.Contains(command.ReviewerRole))
        {
            logger.LogWarning("Unauthorized discrepancy review attempt by user {User} with role {Role}",
                command.ReviewerUserId, command.ReviewerRole);
            throw new UnauthorizedAccessException($"Role '{command.ReviewerRole}' is not authorized to review discrepancies.");
        }

        var discrepancy = await reconciliationRepository.GetDiscrepancyByIdAsync(command.DiscrepancyId, ct)
            ?? throw new DomainException($"Discrepancy with ID '{command.DiscrepancyId}' not found.");

        if (discrepancy.Status == DiscrepancyStatus.Resolved || discrepancy.Status == DiscrepancyStatus.Rejected)
        {
            throw new DomainException($"Discrepancy '{command.DiscrepancyId}' has already been reviewed ({discrepancy.Status}).");
        }

        var decision = new ReviewDecision
        {
            Id = Guid.NewGuid(),
            DiscrepancyId = discrepancy.Id,
            Action = command.Action,
            ReviewerUserId = command.ReviewerUserId,
            ReviewerRole = command.ReviewerRole,
            Notes = command.Notes,
            DecidedAt = DateTimeOffset.UtcNow
        };

        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            discrepancy.Status = command.Action switch
            {
                ReviewAction.ApprovedWithTolerance => DiscrepancyStatus.Resolved,
                ReviewAction.Adjusted => DiscrepancyStatus.Resolved,
                ReviewAction.Rejected => DiscrepancyStatus.Rejected,
                _ => DiscrepancyStatus.UnderReview
            };

            await reconciliationRepository.UpdateDiscrepancyAsync(discrepancy, ct);
            await reconciliationRepository.AddReviewDecisionAsync(decision, ct);

            // Fetch the parent reconciliation result
            var reconResult = await reconciliationRepository.GetResultByIdAsync(discrepancy.ReconciliationResultId, ct);
            if (reconResult != null)
            {
                var invoices = await invoiceRepository.GetByPurchaseOrderNumberAsync(reconResult.PurchaseOrderNumber, ct);
                var invoice = invoices.FirstOrDefault(i => i.InvoiceNumber == reconResult.InvoiceNumber);

                if (command.Action == ReviewAction.ApprovedWithTolerance || command.Action == ReviewAction.Adjusted)
                {
                    // Check if all discrepancies on this result are now Resolved
                    var allDiscrepancies = reconResult.Discrepancies.ToList();
                    var allResolved = allDiscrepancies.All(d => d.Id == discrepancy.Id || d.Status == DiscrepancyStatus.Resolved);

                    if (allResolved)
                    {
                        // Commit planned allocations to GoodsReceiptLines
                        foreach (var alloc in reconResult.Allocations)
                        {
                            var allocated = await grRepository.TryAllocateQuantityAsync(alloc.GoodsReceiptLineId, alloc.AllocatedQuantity, ct);
                            if (!allocated)
                            {
                                logger.LogWarning("Concurrent allocation failed during variance approval for receipt line {LineId}", alloc.GoodsReceiptLineId);
                                throw new DomainException($"Cannot approve discrepancy: Goods receipt line {alloc.GoodsReceiptLineId} does not have sufficient available quantity to allocate.");
                            }
                        }

                        if (invoice != null)
                        {
                            invoice.Status = InvoiceStatus.ApprovedWithVariance;
                            await invoiceRepository.UpdateAsync(invoice, ct);
                        }
                    }
                }
                else if (command.Action == ReviewAction.Rejected)
                {
                    if (invoice != null)
                    {
                        invoice.Status = InvoiceStatus.Rejected;
                        await invoiceRepository.UpdateAsync(invoice, ct);
                    }

                    // Release any allocations previously held
                    foreach (var alloc in reconResult.Allocations)
                    {
                        await grRepository.ReleaseAllocationQuantityAsync(alloc.GoodsReceiptLineId, alloc.AllocatedQuantity, ct);
                    }
                }
            }

            await auditRepository.AddAsync(new AuditEvent
            {
                EntityType = "Discrepancy",
                EntityId = discrepancy.Id.ToString(),
                Action = $"ReviewDecision:{command.Action}",
                UserId = command.ReviewerUserId,
                Timestamp = DateTimeOffset.UtcNow,
                Details = $"Role: {command.ReviewerRole}, Notes: {command.Notes}"
            }, ct);

            await tx.CommitAsync(ct);
            logger.LogInformation("Discrepancy {Id} successfully reviewed: {Action} by {User}",
                discrepancy.Id, command.Action, command.ReviewerUserId);

            return decision;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            logger.LogError(ex, "Failed to save review decision for discrepancy {Id}", discrepancy.Id);
            throw;
        }
    }
}
