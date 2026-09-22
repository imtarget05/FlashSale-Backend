namespace FlashSale.Application.Inventory;

/// <summary>Outcome of a stock reservation attempt.</summary>
public enum ReservationResult
{
    /// <summary>Stock secured — safe to enqueue for fulfillment.</summary>
    Reserved,
    /// <summary>No stock left for this product — reject fast, do not touch the DB.</summary>
    SoldOut,
    /// <summary>This idempotency key already reserved — duplicate request.</summary>
    Duplicate,
    /// <summary>No cached state for this product — caller should seed from the DB.</summary>
    UnknownProduct,
    /// <summary>Reservation tier unreachable — caller should fall back to the DB path.</summary>
    Unavailable
}

/// <summary>
/// Port: the fast-fail inventory reservation tier (ADR-003). Redis-backed in
/// production; the Application layer only sees this contract. Lives under
/// Inventory/ — this is an inventory concern, not a messaging one.
/// </summary>
public interface IStockReservationGateway
{
    Task<ReservationResult> TryReserveAsync(int productId, int quantity, string idempotencyKey);
    Task ReleaseReservationAsync(int productId, int quantity, string idempotencyKey);
    Task SetStockAsync(int productId, int stock);
    Task<int?> GetCachedStockAsync(int productId);
}
