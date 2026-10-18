using Microsoft.EntityFrameworkCore;
using OpsBridge.Application.Connectors;
using OpsBridge.Domain.Connectors;

namespace OpsBridge.Infrastructure.Persistence;

public sealed class EfConnectorRepository(OpsBridgeDbContext db) : IConnectorRepository
{
    public async Task<ConnectorConfig?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await db.Connectors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<ConnectorConfig?> GetByTypeAsync(ConnectorType type, CancellationToken ct = default)
    {
        return await db.Connectors.AsNoTracking().FirstOrDefaultAsync(c => c.Type == type && c.IsActive, ct);
    }

    public async Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.Connectors.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task SaveAsync(ConnectorConfig connector, CancellationToken ct = default)
    {
        var existing = await db.Connectors.FirstOrDefaultAsync(c => c.Id == connector.Id, ct);
        if (existing == null)
        {
            await db.Connectors.AddAsync(connector, ct);
        }
        else
        {
            existing.Name = connector.Name;
            existing.Secret = connector.Secret;
            existing.BaseUrl = connector.BaseUrl;
            existing.IsActive = connector.IsActive;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
