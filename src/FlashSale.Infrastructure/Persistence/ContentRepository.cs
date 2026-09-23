using FlashSale.Application.Persistence;
using FlashSale.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of the content-draft port (spec §8). Every review
/// transition is a GUARDED UPDATE (WHERE Status = expected): the second approve,
/// a publish before approval, or two racing reviewers cannot corrupt the state
/// machine. Publish also applies the approved copy to the product row in the
/// same transaction.
/// </summary>
public sealed class ContentRepository(AppDbContext db) : IContentRepository
{
    public async Task<ProductContentDraft> CreateAsync(ProductContentDraft draft, CancellationToken ct = default)
    {
        db.ProductContentDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
        return draft;
    }

    public async Task<ProductContentDraft?> GetAsync(int id, CancellationToken ct = default) =>
        await db.ProductContentDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<ProductContentDraft?> GetLatestForProductAsync(int productId, CancellationToken ct = default) =>
        await db.ProductContentDrafts.AsNoTracking()
            .Where(d => d.ProductId == productId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ProductContentDraft>> ListReviewQueueAsync(int take, CancellationToken ct = default) =>
        await db.ProductContentDrafts.AsNoTracking()
            .Where(d => d.Status == ProductContentStatus.ReviewRequired)
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<bool> MarkReviewRequiredAsync(int id, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ProductContentDrafts"
            SET    "Status" = 'ReviewRequired'
            WHERE  "Id" = {id}
              AND  "Status" = 'AiGenerated'
            """, ct) == 1;

    public async Task<bool> ApproveAsync(int id, Guid reviewerId, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ProductContentDrafts"
            SET    "Status" = 'Approved',
                   "ReviewedAt" = now(),
                   "ReviewedBy" = {reviewerId}
            WHERE  "Id" = {id}
              AND  "Status" = 'ReviewRequired'
            """, ct) == 1;

    public async Task<bool> RejectAsync(int id, Guid reviewerId, string reason, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ProductContentDrafts"
            SET    "Status" = 'Rejected',
                   "ReviewedAt" = now(),
                   "ReviewedBy" = {reviewerId},
                   "RejectionReason" = {reason}
            WHERE  "Id" = {id}
              AND  "Status" IN ('ReviewRequired', 'Approved')
            """, ct) == 1;

    public async Task<bool> PublishAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Human-approved only: the guard is the state machine's last line of defence.
        var published = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ProductContentDrafts"
            SET    "Status" = 'Published',
                   "PublishedAt" = now()
            WHERE  "Id" = {id}
              AND  "Status" = 'Approved'
            """, ct);
        if (published == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        // Apply the approved short description to the product in the SAME transaction.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Products" p
            SET    "Description" = d."ShortDescription"
            FROM   "ProductContentDrafts" d
            WHERE  d."Id" = {id}
              AND  p."Id" = d."ProductId"
            """, ct);

        await tx.CommitAsync(ct);
        return true;
    }
}