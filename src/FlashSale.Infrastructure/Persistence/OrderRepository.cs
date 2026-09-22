using FlashSale.Application.Messaging;
using FlashSale.Application.Persistence;
using FlashSale.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of the order repository. The atomic conditional
/// UPDATE (ADR-002) guarantees no overselling; decrement + insert share one
/// transaction.
/// </summary>
public sealed class OrderRepository(AppDbContext db) : IOrderRepository
{
    public Task<bool> ExistsAsync(string idempotencyKey, CancellationToken ct = default) =>
        db.Orders.AnyAsync(o => o.IdempotencyKey == idempotencyKey, ct);

    public async Task<bool> PersistAsync(OrderMessage message, CancellationToken ct = default)
    {
        // Defense in depth (Task 2, Part K): the API already returns 400 for
        // Quantity <= 0, but the repository must never mutate stock for it.
        // Without this, a negative qty would satisfy `stock >= qty` and the
        // UPDATE would ADD stock (confirmed by regression test before fix).
        if (message.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(message.Quantity), "Quantity must be positive.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Products"
            SET    "AvailableStock" = "AvailableStock" - {message.Quantity}
            WHERE  "Id" = {message.ProductId}
              AND  "AvailableStock" >= {message.Quantity}
            """, ct);

        if (affected == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        db.Orders.Add(new Order
        {
            ProductId = message.ProductId,
            Quantity = message.Quantity,
            IdempotencyKey = message.IdempotencyKey,
            CreatedAt = message.CreatedAt.UtcDateTime
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
