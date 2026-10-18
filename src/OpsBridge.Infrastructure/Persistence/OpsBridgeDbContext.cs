using Microsoft.EntityFrameworkCore;
using OpsBridge.Domain.Connectors;
using OpsBridge.Domain.DLQ;
using OpsBridge.Domain.InboxOutbox;
using OpsBridge.Domain.Ingestion;
using OpsBridge.Domain.Reconciliation;

namespace OpsBridge.Infrastructure.Persistence;

public class OpsBridgeDbContext(DbContextOptions<OpsBridgeDbContext> options) : DbContext(options)
{
    public DbSet<ConnectorConfig> Connectors => Set<ConnectorConfig>();
    public DbSet<IngestedEvent> IngestedEvents => Set<IngestedEvent>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<ConsumerProcessingState> ConsumerProcessingStates => Set<ConsumerProcessingState>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<DeadLetterEvent> DeadLetterEvents => Set<DeadLetterEvent>();

    // Phase G3 Reconciliation DbSets
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierInvoiceLine> SupplierInvoiceLines => Set<SupplierInvoiceLine>();
    public DbSet<MappingRule> MappingRules => Set<MappingRule>();
    public DbSet<ReconciliationBatch> ReconciliationBatches => Set<ReconciliationBatch>();
    public DbSet<ReconciliationResult> ReconciliationResults => Set<ReconciliationResult>();
    public DbSet<ReconciliationAllocation> ReconciliationAllocations => Set<ReconciliationAllocation>();
    public DbSet<Discrepancy> Discrepancies => Set<Discrepancy>();
    public DbSet<ReviewDecision> ReviewDecisions => Set<ReviewDecision>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

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

        // InboxMessage (Composite PK guarantees single execution per ConsumerGroup for webhooks)
        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.HasKey(i => new { i.MessageId, i.ConsumerGroup });
            entity.Property(i => i.ConsumerGroup).HasMaxLength(100);
            entity.Property(i => i.EventType).HasMaxLength(100);
            entity.Property(i => i.Source).HasMaxLength(200);
            entity.Property(i => i.PayloadHash).HasMaxLength(100);
        });

        // ConsumerProcessingState (Decoupled downstream consumer tracking)
        modelBuilder.Entity<ConsumerProcessingState>(entity =>
        {
            entity.HasKey(c => new { c.EventId, c.ConsumerGroup });
            entity.Property(c => c.ConsumerGroup).HasMaxLength(100).IsRequired();
            entity.Property(c => c.EventType).HasMaxLength(100).IsRequired();
            entity.Property(c => c.Status).HasConversion<string>();
            entity.Property(c => c.ErrorMessage).HasMaxLength(2000);
        });

        // OutboxMessage
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.HasIndex(o => o.ProcessedAt);
            entity.HasIndex(o => o.Status);
            entity.Property(o => o.EventType).HasMaxLength(100).IsRequired();
            entity.Property(o => o.Topic).HasMaxLength(150).IsRequired();
            entity.Property(o => o.Status).HasMaxLength(50).IsRequired();
            entity.Property(o => o.Payload).HasColumnType("text").IsRequired();
        });

        // DeadLetterEvent
        modelBuilder.Entity<DeadLetterEvent>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.HasIndex(d => d.EventId);
            entity.HasIndex(d => d.IsReplayed);
            entity.Property(d => d.EventType).HasMaxLength(100).IsRequired();
            entity.Property(d => d.Topic).HasMaxLength(150).IsRequired();
            entity.Property(d => d.Reason).HasMaxLength(2000).IsRequired();
            entity.Property(d => d.Payload).HasColumnType("text").IsRequired();
        });

        // PurchaseOrder & Lines
        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => p.OrderNumber).IsUnique();
            entity.Property(p => p.OrderNumber).HasMaxLength(100).IsRequired();
            entity.Property(p => p.SupplierCode).HasMaxLength(100).IsRequired();
            entity.Property(p => p.SupplierName).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Currency).HasMaxLength(10).IsRequired();
            entity.Property(p => p.Status).HasConversion<string>();
            entity.Property(p => p.TotalAmount).HasPrecision(18, 2);

            entity.HasMany(p => p.Lines)
                .WithOne()
                .HasForeignKey(l => l.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PurchaseOrderLine>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => new { l.PurchaseOrderId, l.LineNumber }).IsUnique();
            entity.Property(l => l.ItemSku).HasMaxLength(100).IsRequired();
            entity.Property(l => l.Description).HasMaxLength(300);
            entity.Property(l => l.UnitOfMeasure).HasMaxLength(20).IsRequired();
            entity.Property(l => l.OrderedQuantity).HasPrecision(18, 4);
            entity.Property(l => l.UnitPrice).HasPrecision(18, 2);
            entity.Property(l => l.TotalAmount).HasPrecision(18, 2);
        });

        // GoodsReceipt & Lines
        modelBuilder.Entity<GoodsReceipt>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.HasIndex(g => g.ReceiptNumber).IsUnique();
            entity.HasIndex(g => g.PurchaseOrderNumber);
            entity.Property(g => g.ReceiptNumber).HasMaxLength(100).IsRequired();
            entity.Property(g => g.PurchaseOrderNumber).HasMaxLength(100).IsRequired();
            entity.Property(g => g.SupplierCode).HasMaxLength(100).IsRequired();

            entity.HasMany(g => g.Lines)
                .WithOne()
                .HasForeignKey(l => l.GoodsReceiptId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GoodsReceiptLine>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => new { l.GoodsReceiptId, l.LineNumber }).IsUnique();
            entity.Property(l => l.ItemSku).HasMaxLength(100).IsRequired();
            entity.Property(l => l.UnitOfMeasure).HasMaxLength(20).IsRequired();
            entity.Property(l => l.ReceivedQuantity).HasPrecision(18, 4);
            entity.Property(l => l.AllocatedQuantity).HasPrecision(18, 4);
        });

        // SupplierInvoice & Lines
        modelBuilder.Entity<SupplierInvoice>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.HasIndex(i => new { i.SupplierCode, i.InvoiceNumber }).IsUnique();
            entity.HasIndex(i => i.PurchaseOrderNumber);
            entity.Property(i => i.InvoiceNumber).HasMaxLength(100).IsRequired();
            entity.Property(i => i.PurchaseOrderNumber).HasMaxLength(100).IsRequired();
            entity.Property(i => i.SupplierCode).HasMaxLength(100).IsRequired();
            entity.Property(i => i.Currency).HasMaxLength(10).IsRequired();
            entity.Property(i => i.Status).HasConversion<string>();
            entity.Property(i => i.SubTotal).HasPrecision(18, 2);
            entity.Property(i => i.TaxAmount).HasPrecision(18, 2);
            entity.Property(i => i.DiscountAmount).HasPrecision(18, 2);
            entity.Property(i => i.AdditionalCharges).HasPrecision(18, 2);
            entity.Property(i => i.TotalAmount).HasPrecision(18, 2);

            entity.HasMany(i => i.Lines)
                .WithOne()
                .HasForeignKey(l => l.SupplierInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SupplierInvoiceLine>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => new { l.SupplierInvoiceId, l.LineNumber }).IsUnique();
            entity.Property(l => l.ItemSku).HasMaxLength(100).IsRequired();
            entity.Property(l => l.Description).HasMaxLength(300);
            entity.Property(l => l.UnitOfMeasure).HasMaxLength(20).IsRequired();
            entity.Property(l => l.InvoicedQuantity).HasPrecision(18, 4);
            entity.Property(l => l.UnitPrice).HasPrecision(18, 2);
            entity.Property(l => l.TaxRate).HasPrecision(18, 4);
            entity.Property(l => l.LineTotal).HasPrecision(18, 2);
            entity.Property(l => l.MatchedQuantity).HasPrecision(18, 4);
        });

        // MappingRule
        modelBuilder.Entity<MappingRule>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => new { m.RuleType, m.SourceValue, m.SupplierCode });
            entity.Property(m => m.RuleType).HasConversion<string>();
            entity.Property(m => m.SourceValue).HasMaxLength(100).IsRequired();
            entity.Property(m => m.TargetValue).HasMaxLength(100).IsRequired();
            entity.Property(m => m.SupplierCode).HasMaxLength(100);
            entity.Property(m => m.ConversionFactor).HasPrecision(18, 4);
        });

        // ReconciliationBatch & Result & Allocation
        modelBuilder.Entity<ReconciliationBatch>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.HasIndex(b => b.BatchNumber).IsUnique();
            entity.Property(b => b.BatchNumber).HasMaxLength(100).IsRequired();
            entity.Property(b => b.Status).HasConversion<string>();
            entity.Property(b => b.InitiatedBy).HasMaxLength(100).IsRequired();

            entity.HasMany(b => b.Results)
                .WithOne()
                .HasForeignKey(r => r.BatchId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReconciliationResult>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.PurchaseOrderNumber);
            entity.HasIndex(r => r.InvoiceNumber);
            entity.Property(r => r.PurchaseOrderNumber).HasMaxLength(100).IsRequired();
            entity.Property(r => r.InvoiceNumber).HasMaxLength(100).IsRequired();
            entity.Property(r => r.Status).HasConversion<string>();
            entity.Property(r => r.TotalQuantityOrdered).HasPrecision(18, 4);
            entity.Property(r => r.TotalQuantityReceived).HasPrecision(18, 4);
            entity.Property(r => r.TotalQuantityInvoiced).HasPrecision(18, 4);
            entity.Property(r => r.TotalAmountVariance).HasPrecision(18, 2);

            entity.HasMany(r => r.Allocations)
                .WithOne()
                .HasForeignKey(a => a.ReconciliationResultId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(r => r.Discrepancies)
                .WithOne()
                .HasForeignKey(d => d.ReconciliationResultId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReconciliationAllocation>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => a.ReconciliationResultId);
            entity.Property(a => a.ItemSku).HasMaxLength(100).IsRequired();
            entity.Property(a => a.AllocatedQuantity).HasPrecision(18, 4);
            entity.Property(a => a.UnitPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Discrepancy>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.HasIndex(d => d.ReconciliationResultId);
            entity.HasIndex(d => d.Status);
            entity.Property(d => d.Type).HasConversion<string>();
            entity.Property(d => d.Severity).HasConversion<string>();
            entity.Property(d => d.Status).HasConversion<string>();
            entity.Property(d => d.ItemSku).HasMaxLength(100);
            entity.Property(d => d.ExpectedValue).HasMaxLength(200);
            entity.Property(d => d.ActualValue).HasMaxLength(200);
            entity.Property(d => d.VarianceAmount).HasPrecision(18, 4);
            entity.Property(d => d.Details).HasMaxLength(2000);

            entity.HasOne(d => d.ReviewDecision)
                .WithOne()
                .HasForeignKey<ReviewDecision>(r => r.DiscrepancyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReviewDecision>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Action).HasConversion<string>();
            entity.Property(r => r.ReviewerUserId).HasMaxLength(100).IsRequired();
            entity.Property(r => r.ReviewerRole).HasMaxLength(50).IsRequired();
            entity.Property(r => r.Notes).HasMaxLength(1000);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => new { a.EntityType, a.EntityId });
            entity.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(100).IsRequired();
            entity.Property(a => a.UserId).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Details).HasMaxLength(2000);
        });
    }
}
