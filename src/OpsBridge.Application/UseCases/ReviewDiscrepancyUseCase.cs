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
