namespace FlashSale.Application.Inventory;

public enum ReservationResult
{
    Reserved,
    SoldOut,
    Duplicate
}

public interface IStockReservationGateway
{
    Task<ReservationResult> TryReserveAsync(int productId, int quantity, string idempotencyKey);
    Task ReleaseReservationAsync(int productId, int quantity, string idempotencyKey);
    Task SetStockAsync(int productId, int stock);
    Task<int?> GetCachedStockAsync(int productId);
}

public static class LowStockRule
{
    public static bool IsLowStock(int stock, int threshold)
    {
        if (threshold <= 0) return stock == 0;
        return stock <= threshold;
    }
}
