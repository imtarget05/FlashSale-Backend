using Microsoft.EntityFrameworkCore;
using OpsBridge.Application.Ingestion;
using OpsBridge.Domain.Ingestion;

namespace OpsBridge.Infrastructure.Persistence;

public sealed class EfIngestedEventRepository(OpsBridgeDbContext db) : IIngestedEventRepository
{
    public async Task<bool> ExistsAsync(Guid eventId, CancellationToken ct = default)
    {
        return await db.IngestedEvents.AnyAsync(e => e.EventId == eventId, ct);
    }

    public async Task AddAsync(IngestedEvent ev, CancellationToken ct = default)
    {
        await db.IngestedEvents.AddAsync(ev, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid eventId, EventProcessingStatus status, string? errorMessage = null, CancellationToken ct = default)
    {
        var ev = await db.IngestedEvents.FirstOrDefaultAsync(e => e.EventId == eventId, ct);
        if (ev != null)
        {
            ev.Status = status;
            ev.ErrorMessage = errorMessage;
            if (status == EventProcessingStatus.Completed || status == EventProcessingStatus.Failed)
            {
                ev.ProcessedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IngestedEvent?> GetByIdAsync(Guid eventId, CancellationToken ct = default)
    {
        return await db.IngestedEvents.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId, ct);
    }

    public async Task<IReadOnlyList<IngestedEvent>> GetRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        return await db.IngestedEvents
            .AsNoTracking()
            .OrderByDescending(e => e.ReceivedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
