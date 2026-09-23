using FlashSale.Domain.Content;

namespace FlashSale.Application.Persistence;

/// <summary>
/// Port: AI content drafts (spec §8) with GUARDED transitions. Approve/reject/
/// publish are conditional UPDATEs (WHERE Status = expected), so a duplicated
/// or racing request cannot double-approve or publish unapproved content.
/// </summary>
public interface IContentRepository
{
    Task<ProductContentDraft> CreateAsync(ProductContentDraft draft, CancellationToken ct = default);
    Task<ProductContentDraft?> GetAsync(int id, CancellationToken ct = default);
    Task<ProductContentDraft?> GetLatestForProductAsync(int productId, CancellationToken ct = default);

    /// <summary>Drafts waiting for a human decision, newest first (spec §13).</summary>
    Task<IReadOnlyList<ProductContentDraft>> ListReviewQueueAsync(int take, CancellationToken ct = default);

    /// <summary>AiGenerated → ReviewRequired (the mandatory review gate).</summary>
    Task<bool> MarkReviewRequiredAsync(int id, CancellationToken ct = default);

    /// <summary>ReviewRequired → Approved.</summary>
    Task<bool> ApproveAsync(int id, Guid reviewerId, CancellationToken ct = default);

    /// <summary>ReviewRequired|Approved → Rejected (with reason).</summary>
    Task<bool> RejectAsync(int id, Guid reviewerId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Approved → Published AND applies the short description to the product row
    /// in the same transaction. Returns false when the draft was not approved.
    /// </summary>
    Task<bool> PublishAsync(int id, CancellationToken ct = default);
}