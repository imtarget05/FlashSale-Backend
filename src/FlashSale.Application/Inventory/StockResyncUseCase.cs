namespace FlashSale.Application.Inventory;

using FlashSale.Application.Persistence;
using Microsoft.Extensions.Logging;

public sealed class StockResyncUseCase
{
    private readonly IOrderReadModel _readModel;
    private readonly IStockReservationGateway _gateway;
    private readonly ILogger<StockResyncUseCase> _logger;

    public StockResyncUseCase(
        IOrderReadModel readModel,
        IStockReservationGateway gateway,
        ILogger<StockResyncUseCase> logger)
    {
        _readModel = readModel;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<StockResyncResult?> ExecuteAsync(int productId, bool dryRun, CancellationToken ct = default)
    {
        var postgres = await _readModel.GetStockAsync(productId, ct);
        var redis = await _gateway.GetCachedStockAsync(productId);

        if (postgres is null && redis is null) return null;

        var p = postgres ?? 0;
        var r = redis ?? 0;
        var drift = p - r;

        if (drift == 0) return new StockResyncResult { ProductId = productId, Redis = r, Postgres = p, Fixed = false };

        if (!dryRun)
        {
            await _gateway.SetStockAsync(productId, p);
            return new StockResyncResult { ProductId = productId, Redis = p, Postgres = p, Fixed = true };
        }

        return new StockResyncResult { ProductId = productId, Redis = r, Postgres = p, Fixed = false };
    }
}

public sealed class StockResyncResult
{
    public int ProductId { get; init; }
    public int Redis { get; init; }
    public int Postgres { get; init; }
    public int Drift => Postgres - Redis;
    public bool Fixed { get; init; }
}
