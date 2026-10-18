using Microsoft.EntityFrameworkCore;
using OpsBridge.Application.InboxOutbox;
using OpsBridge.Domain.InboxOutbox;

namespace OpsBridge.Infrastructure.Persistence;

public sealed class EfConsumerProcessingRepository(OpsBridgeDbContext db) : IConsumerProcessingRepository
{
    public async Task<bool> HasBeenProcessedAsync(Guid eventId, string consumerGroup, CancellationToken ct = default)
    {
        return await db.ConsumerProcessingStates.AnyAsync(
            c => c.EventId == eventId && c.ConsumerGroup == consumerGroup && c.Status == ConsumerStatus.Completed, ct);
    }

    public async Task<ConsumerProcessingState?> GetAsync(Guid eventId, string consumerGroup, CancellationToken ct = default)
    {
        return await db.ConsumerProcessingStates.FirstOrDefaultAsync(
            c => c.EventId == eventId && c.ConsumerGroup == consumerGroup, ct);
    }

    public async Task RecordAsync(ConsumerProcessingState state, CancellationToken ct = default)
    {
        var existing = await db.ConsumerProcessingStates.FirstOrDefaultAsync(
            c => c.EventId == state.EventId && c.ConsumerGroup == state.ConsumerGroup, ct);

        if (existing == null)
        {
            await db.ConsumerProcessingStates.AddAsync(state, ct);
        }
        else
        {
            existing.Status = state.Status;
            existing.ProcessedAt = state.ProcessedAt;
            existing.ErrorMessage = state.ErrorMessage;
        }
    }
}
