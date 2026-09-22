namespace FlashSale.Application.Persistence;

/// <summary>
/// Port: verify that the primary data store is reachable.
/// Used by the readiness probe so the presentation layer never
/// depends directly on a concrete <c>DbContext</c>.
/// </summary>
public interface IDatabaseHealthCheck
{
    Task<bool> CanConnectAsync(CancellationToken ct = default);
}
