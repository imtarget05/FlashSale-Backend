using Microsoft.EntityFrameworkCore.Storage;
using OpsBridge.Application.Common;

namespace OpsBridge.Infrastructure.Persistence;

public sealed class EfUnitOfWork(OpsBridgeDbContext db) : IUnitOfWork
{
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        return new EfUnitOfWorkTransaction(tx, db);
    }

    private sealed class EfUnitOfWorkTransaction(IDbContextTransaction tx, OpsBridgeDbContext db) : IUnitOfWorkTransaction
    {
        public async Task CommitAsync(CancellationToken ct = default)
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        public async Task RollbackAsync(CancellationToken ct = default)
        {
            await tx.RollbackAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            await tx.DisposeAsync();
        }
    }
}
