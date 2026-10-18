using OpsBridge.Domain.Ingestion;

namespace OpsBridge.Application.Ingestion;

public interface IIngestedEventRepository
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken ct = default);
    Task AddAsync(IngestedEvent ev, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid eventId, EventProcessingStatus status, string? errorMessage = null, CancellationToken ct = default);
    Task<IngestedEvent?> GetByIdAsync(Guid eventId, CancellationToken ct = default);
    Task<IReadOnlyList<IngestedEvent>> GetRecentAsync(int limit = 50, CancellationToken ct = default);
}
