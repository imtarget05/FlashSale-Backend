using Microsoft.EntityFrameworkCore;
using OpsBridge.Domain.Connectors;
using OpsBridge.Domain.InboxOutbox;
using OpsBridge.Domain.Ingestion;

namespace OpsBridge.Infrastructure.Persistence;

public class OpsBridgeDbContext(DbContextOptions<OpsBridgeDbContext> options) : DbContext(options)
{
    public DbSet<ConnectorConfig> Connectors => Set<ConnectorConfig>();
    public DbSet<IngestedEvent> IngestedEvents => Set<IngestedEvent>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ConnectorConfig
        modelBuilder.Entity<ConnectorConfig>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.Type).IsUnique();
            entity.Property(c => c.Name).HasMaxLength(150).IsRequired();
            entity.Property(c => c.Secret).HasMaxLength(256).IsRequired();
            entity.Property(c => c.BaseUrl).HasMaxLength(500);
            entity.Property(c => c.Type).HasConversion<string>();
        });

        // IngestedEvent
        modelBuilder.Entity<IngestedEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SchemaVersion).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.RawPayload).HasColumnType("text").IsRequired();
        });

        // InboxMessage (Composite key: MessageId + ConsumerGroup)
        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.HasKey(i => new { i.MessageId, i.ConsumerGroup });
            entity.Property(i => i.ConsumerGroup).HasMaxLength(100);
            entity.Property(i => i.EventType).HasMaxLength(100);
            entity.Property(i => i.Source).HasMaxLength(200);
        });

        // OutboxMessage
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.HasIndex(o => o.ProcessedAt);
            entity.Property(o => o.EventType).HasMaxLength(100).IsRequired();
            entity.Property(o => o.Topic).HasMaxLength(150).IsRequired();
            entity.Property(o => o.Payload).HasColumnType("text").IsRequired();
        });
    }
}
