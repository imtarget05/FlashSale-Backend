using OpsBridge.Domain.Connectors;

namespace OpsBridge.Application.Connectors;

public interface IConnectorRepository
{
    Task<ConnectorConfig?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ConnectorConfig?> GetByTypeAsync(ConnectorType type, CancellationToken ct = default);
    Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default);
    Task SaveAsync(ConnectorConfig connector, CancellationToken ct = default);
}
