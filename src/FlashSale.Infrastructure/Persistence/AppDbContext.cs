using FlashSale.Application.Persistence;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Outbox;
using FlashSale.Domain.Saga;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<OutboxMessageRow> OutboxMessages { get; set; }
    public DbSet<CheckoutSagaState> CheckoutSagaStates { get; set; }
    public DbSet<AutomationRun> AutomationRuns { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedOnAdd();
            b.HasIndex(o => o.IdempotencyKey).IsUnique();
            b.Property(o => o.Status).HasConversion<string>();
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("products");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(m => m.MessageId);
            b.HasIndex(m => new { m.EventType, m.AggregateType, m.AggregateId, m.CorrelationId });
        });

        modelBuilder.Entity<CheckoutSagaState>(b =>
        {
            b.ToTable("checkout_saga_states");
            b.HasKey(s => s.SagaId);
            b.HasIndex(s => s.IdempotencyKey).IsUnique();
            b.Property(s => s.Status).HasConversion<string>();
            b.Property(s => s.InventoryStatus).HasConversion<string>();
            b.Property(s => s.PaymentStatus).HasConversion<string>();
        });

        modelBuilder.Entity<AutomationRun>(b =>
        {
            b.ToTable("automation_runs");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedOnAdd();
        });
    }
}

public sealed class AutomationRun
{
    public int Id { get; set; }
    public string RunId { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
