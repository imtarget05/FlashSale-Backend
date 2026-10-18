using Microsoft.Extensions.Logging.Abstractions;
using OpsBridge.Application.Common;
using OpsBridge.Application.Reconciliation;
using OpsBridge.Application.UseCases;
using OpsBridge.Domain;
using OpsBridge.Domain.Reconciliation;

namespace OpsBridge.UnitTests;

public class ReconciliationMatchingTests
{
    private readonly ThreeWayMatchingEngine _engine = new();
    private readonly TolerancePolicy _defaultTolerance = new(
        PriceTolerancePercentage: 0.01m,    // 1%
        QuantityTolerancePercentage: 0.00m, // 0%
        QuantityToleranceAbsolute: 0.00m
    );

    // 1. Perfect three-way match
    [Fact]
    public void Match_PerfectThreeWayMatch_ReturnsMatched()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m);
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 100m);
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 100m, 20000m);

        var result = _engine.Match(po, [gr], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Matched, result.Status);
        Assert.Empty(result.Discrepancies);
        Assert.Single(result.Allocations);
        Assert.Equal(100m, result.Allocations[0].AllocatedQuantity);
        Assert.Equal(100m, gr.Lines[0].AllocatedQuantity);
        Assert.Equal(0m, gr.Lines[0].AvailableQuantity);
    }

    // 2. Partial goods receipt
    [Fact]
    public void Match_PartialGoodsReceipt_FlagsQuantityMismatch()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m);
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 60m); // only 60 received
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 100m, 20000m);

        var result = _engine.Match(po, [gr], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Discrepancy, result.Status);
        var disc = Assert.Single(result.Discrepancies);
        Assert.Equal(DiscrepancyType.QuantityMismatch, disc.Type);
        Assert.Equal(40m, disc.VarianceAmount); // 100 - 60 = 40
        Assert.Empty(result.Allocations); // No allocations committed on discrepancy
    }

    // 3. Multiple goods receipts (1 PO -> N GRs)
    [Fact]
    public void Match_MultipleGoodsReceipts_CumulativeAllocation_ReturnsMatched()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m);
        var gr1 = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 60m);
        var gr2 = CreateGr("GR-002", "PO-1001", "SUPP-01", "RM-008", 30m);
        var gr3 = CreateGr("GR-003", "PO-1001", "SUPP-01", "RM-008", 10m);
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 100m, 20000m);

        var result = _engine.Match(po, [gr1, gr2, gr3], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Matched, result.Status);
        Assert.Empty(result.Discrepancies);
        Assert.Equal(3, result.Allocations.Count);
        Assert.Equal(60m, gr1.Lines[0].AllocatedQuantity);
        Assert.Equal(30m, gr2.Lines[0].AllocatedQuantity);
        Assert.Equal(10m, gr3.Lines[0].AllocatedQuantity);
    }

    // 4. Multiple invoices (Partial invoicing & non-overlapping allocation)
    [Fact]
    public void Match_MultipleInvoices_PartialInvoicing_AllocatesAvailableQuantity()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m);
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 100m);

        // First invoice for 60 PCS
        var inv1 = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 60m, 20000m);
        var res1 = _engine.Match(po, [gr], inv1, [], _defaultTolerance);
        Assert.Equal(MatchStatus.Matched, res1.Status);
        Assert.Equal(60m, gr.Lines[0].AllocatedQuantity);
        Assert.Equal(40m, gr.Lines[0].AvailableQuantity);

        // Second invoice for remaining 40 PCS
        var inv2 = CreateInvoice("INV-002", "PO-1001", "SUPP-01", "RM-008", 40m, 20000m);
        var res2 = _engine.Match(po, [gr], inv2, [], _defaultTolerance);
        Assert.Equal(MatchStatus.Matched, res2.Status);
        Assert.Equal(100m, gr.Lines[0].AllocatedQuantity);
        Assert.Equal(0m, gr.Lines[0].AvailableQuantity);

        // Third invoice attempting to over-invoice 10 PCS
        var inv3 = CreateInvoice("INV-003", "PO-1001", "SUPP-01", "RM-008", 10m, 20000m);
        var res3 = _engine.Match(po, [gr], inv3, [], _defaultTolerance);
        Assert.Equal(MatchStatus.Discrepancy, res3.Status);
        var disc = Assert.Single(res3.Discrepancies);
        Assert.Equal(DiscrepancyType.QuantityMismatch, disc.Type);
    }

    // 5. Quantity mismatch (Invoiced exceeds PO & GR)
    [Fact]
    public void Match_QuantityMismatch_InvoicedExceedsAvailableReceipts()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 50m, 20000m);
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 50m);
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 75m, 20000m);

        var result = _engine.Match(po, [gr], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Discrepancy, result.Status);
        var disc = Assert.Single(result.Discrepancies);
        Assert.Equal(DiscrepancyType.QuantityMismatch, disc.Type);
        Assert.Equal(25m, disc.VarianceAmount);
    }

    // 6. Price mismatch (Beyond tolerance)
    [Fact]
    public void Match_PriceMismatch_ExceedsTolerance_FlagsPriceMismatch()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m); // 20,000 VND
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 100m);
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 100m, 25000m); // 25,000 VND (+25%)

        var result = _engine.Match(po, [gr], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Discrepancy, result.Status);
        var disc = Assert.Single(result.Discrepancies);
        Assert.Equal(DiscrepancyType.PriceMismatch, disc.Type);
        Assert.Equal(500000m, disc.VarianceAmount); // (25000 - 20000) * 100 = 500,000 VND
    }

    // 7. Duplicate invoice detection
    [Fact]
    public async Task Ingestion_DuplicateInvoice_DetectedInRepository()
    {
        var fakeRepo = new FakeSupplierInvoiceRepository();
        var inv1 = CreateInvoice("INV-DUP-01", "PO-1001", "SUPP-01", "RM-008", 100m, 20000m);
        await fakeRepo.AddAsync(inv1);

        var existing = await fakeRepo.GetByNumberAndSupplierAsync("INV-DUP-01", "SUPP-01");
        Assert.NotNull(existing);

        var duplicateAttempt = await fakeRepo.GetByNumberAndSupplierAsync("INV-DUP-01", "SUPP-01");
        Assert.Equal(existing.Id, duplicateAttempt?.Id);
    }

    // 8. Unknown SKU on invoice line
    [Fact]
    public void Match_UnknownSku_FlagsUnmatchedInvoiceLine()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m);
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 100m);
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "UNKNOWN-SKU-999", 100m, 20000m);

        var result = _engine.Match(po, [gr], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Discrepancy, result.Status);
        var disc = Assert.Single(result.Discrepancies);
        Assert.Equal(DiscrepancyType.UnmatchedInvoiceLine, disc.Type);
    }

    // 9. Valid UOM conversion (1 BOX = 10 PCS)
    [Fact]
    public void Match_UomConversion_ValidRule_NormalizesCorrectly()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m, uom: "PCS");
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 100m, uom: "PCS");

        // Supplier invoices 10 BOX @ 200,000 VND per BOX
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 10m, 200000m, uom: "BOX");

        var rules = new List<MappingRule>
        {
            new()
            {
                RuleType = MappingRuleType.UomConversion,
                SourceValue = "BOX",
                TargetValue = "PCS",
                ConversionFactor = 10.0m, // 1 BOX = 10 PCS
                IsActive = true
            }
        };

        var result = _engine.Match(po, [gr], inv, rules, _defaultTolerance);

        Assert.Equal(MatchStatus.Matched, result.Status);
        Assert.Empty(result.Discrepancies);
        Assert.Single(result.Allocations);
        Assert.Equal(100m, result.Allocations[0].AllocatedQuantity);
        Assert.Equal(20000m, result.Allocations[0].UnitPrice);
    }

    // 10. Missing PO
    [Fact]
    public void Match_MissingPO_FlagsMissingPurchaseOrder()
    {
        var gr = CreateGr("GR-001", "PO-NONEXISTENT", "SUPP-01", "RM-008", 100m);
        var inv = CreateInvoice("INV-001", "PO-NONEXISTENT", "SUPP-01", "RM-008", 100m, 20000m);

        var result = _engine.Match(null, [gr], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Discrepancy, result.Status);
        var disc = Assert.Single(result.Discrepancies);
        Assert.Equal(DiscrepancyType.MissingPurchaseOrder, disc.Type);
    }

    // 11. Missing Goods Receipt
    [Fact]
    public void Match_MissingReceipt_FlagsMissingGoodsReceipt()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m);
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 100m, 20000m);

        var result = _engine.Match(po, [], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Discrepancy, result.Status);
        Assert.Contains(result.Discrepancies, d => d.Type == DiscrepancyType.MissingGoodsReceipt);
    }

    // 12. Price tolerance boundary check
    [Fact]
    public void Match_PriceToleranceBoundary_AllowsWithinTolerance()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m); // Base 20,000 VND
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 100m);

        var policy1Pct = new TolerancePolicy(PriceTolerancePercentage: 0.01m); // 1% tolerance allows up to 20,200 VND

        // Exactly at boundary (+1% = 20,200) -> Matched
        var invBoundary = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 100m, 20200m);
        var resBoundary = _engine.Match(po, [gr], invBoundary, [], policy1Pct);
        Assert.Equal(MatchStatus.Matched, resBoundary.Status);

        // Reset GR allocation
        gr.Lines[0].AllocatedQuantity = 0m;

        // Exceeds boundary (+1.5% = 20,300) -> Discrepancy
        var invExceeded = CreateInvoice("INV-002", "PO-1001", "SUPP-01", "RM-008", 100m, 20300m);
        var resExceeded = _engine.Match(po, [gr], invExceeded, [], policy1Pct);
        Assert.Equal(MatchStatus.Discrepancy, resExceeded.Status);
        Assert.Contains(resExceeded.Discrepancies, d => d.Type == DiscrepancyType.PriceMismatch);
    }

    // 13. Quantity tolerance boundary check
    [Fact]
    public void Match_QuantityToleranceBoundary_AllowsWithinTolerance()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m);
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 98m); // Received 98

        // Absolute allowance of 2 PCS
        var policyAbs2 = new TolerancePolicy(QuantityToleranceAbsolute: 2.0m);
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 100m, 20000m);

        var result = _engine.Match(po, [gr], inv, [], policyAbs2);
        Assert.Equal(MatchStatus.Matched, result.Status);
        Assert.Empty(result.Discrepancies);
    }

    // 14. Reference Scenario from Specification: PO-1001, GR1 (60), GR2 (30), Inv (100) -> Discrepancy, add GR3 (10) -> Matched!
    [Fact]
    public void Match_ReferenceScenario_Po1001_CumulativeReceipts_MatchesOnRerun()
    {
        // Step 1: PO-1001 (100 PCS @ 20,000 VND)
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m);

        // Step 2: GR 1 (60 PCS) & GR 2 (30 PCS) -> Total received: 90 PCS
        var gr1 = CreateGr("GR-1", "PO-1001", "SUPP-01", "RM-008", 60m);
        var gr2 = CreateGr("GR-2", "PO-1001", "SUPP-01", "RM-008", 30m);

        // Step 3: Supplier Invoice (100 PCS @ 20,000 VND)
        var inv = CreateInvoice("INV-9001", "PO-1001", "SUPP-01", "RM-008", 100m, 20000m);

        // Step 4: First match run
        var firstRun = _engine.Match(po, [gr1, gr2], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Discrepancy, firstRun.Status);
        var disc = Assert.Single(firstRun.Discrepancies);
        Assert.Equal(DiscrepancyType.QuantityMismatch, disc.Type);
        Assert.Equal(10m, disc.VarianceAmount); // 10 PCS variance
        Assert.Empty(firstRun.Allocations); // No partial allocation committed yet

        // Step 5: Deliver remaining 10 PCS in GR 3
        var gr3 = CreateGr("GR-3", "PO-1001", "SUPP-01", "RM-008", 10m);

        // Step 6: Rerun reconciliation with all three receipts
        var secondRun = _engine.Match(po, [gr1, gr2, gr3], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Matched, secondRun.Status);
        Assert.Empty(secondRun.Discrepancies);
        Assert.Equal(3, secondRun.Allocations.Count);
        Assert.Equal(60m, gr1.Lines[0].AllocatedQuantity);
        Assert.Equal(30m, gr2.Lines[0].AllocatedQuantity);
        Assert.Equal(10m, gr3.Lines[0].AllocatedQuantity);
        Assert.Equal(0m, gr1.Lines[0].AvailableQuantity);
        Assert.Equal(0m, gr2.Lines[0].AvailableQuantity);
        Assert.Equal(0m, gr3.Lines[0].AvailableQuantity);
    }

    // 15. Review Discrepancy with Authorized Role (FinanceReviewer / Operator)
    [Fact]
    public async Task ReviewDiscrepancy_AuthorizedRole_ResolvesDiscrepancy()
    {
        var fakeReconRepo = new FakeReconciliationRepository();
        var fakeAuditRepo = new FakeAuditEventRepository();
        var fakeUow = new FakeUnitOfWork();

        var discrepancy = new Discrepancy
        {
            Id = Guid.NewGuid(),
            Type = DiscrepancyType.QuantityMismatch,
            Status = DiscrepancyStatus.Open,
            VarianceAmount = 10m
        };
        await fakeReconRepo.AddDiscrepancyAsync(discrepancy);

        var useCase = new ReviewDiscrepancyUseCase(fakeReconRepo, fakeAuditRepo, fakeUow, NullLogger<ReviewDiscrepancyUseCase>.Instance);

        var command = new ReviewDiscrepancyCommand(
            DiscrepancyId: discrepancy.Id,
            Action: ReviewAction.ApprovedWithTolerance,
            ReviewerUserId: "Accountant-01",
            ReviewerRole: OpsBridgeRoles.FinanceReviewer,
            Notes: "Approved within exceptional quarter-end allowance."
        );

        var decision = await useCase.ExecuteAsync(command);

        Assert.NotNull(decision);
        Assert.Equal(ReviewAction.ApprovedWithTolerance, decision.Action);
        Assert.Equal(DiscrepancyStatus.Resolved, discrepancy.Status);
        Assert.Single(fakeAuditRepo.Events);
    }

    // 16. Review Discrepancy with Unauthorized Role throws exception
    [Fact]
    public async Task ReviewDiscrepancy_UnauthorizedRole_ThrowsUnauthorizedException()
    {
        var fakeReconRepo = new FakeReconciliationRepository();
        var fakeAuditRepo = new FakeAuditEventRepository();
        var fakeUow = new FakeUnitOfWork();

        var discrepancy = new Discrepancy
        {
            Id = Guid.NewGuid(),
            Type = DiscrepancyType.PriceMismatch,
            Status = DiscrepancyStatus.Open
        };
        await fakeReconRepo.AddDiscrepancyAsync(discrepancy);

        var useCase = new ReviewDiscrepancyUseCase(fakeReconRepo, fakeAuditRepo, fakeUow, NullLogger<ReviewDiscrepancyUseCase>.Instance);

        var command = new ReviewDiscrepancyCommand(
            DiscrepancyId: discrepancy.Id,
            Action: ReviewAction.ApprovedWithTolerance,
            ReviewerUserId: "Viewer-01",
            ReviewerRole: "Guest", // Unauthorized role
            Notes: "Attempted unauthorized review."
        );

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => useCase.ExecuteAsync(command));
    }

    // 17. Currency mismatch creates discrepancy
    [Fact]
    public void Match_CurrencyMismatch_FlagsCurrencyMismatch()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m, currency: "VND");
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 100m);
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 100m, 1m, currency: "USD"); // USD vs VND

        var result = _engine.Match(po, [gr], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Discrepancy, result.Status);
        Assert.Contains(result.Discrepancies, d => d.Type == DiscrepancyType.CurrencyMismatch);
    }

    // 18. Invalid UOM Conversion with no rule
    [Fact]
    public void Match_InvalidUomConversion_NoRule_FlagsUomConversionError()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m, uom: "PCS");
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 100m, uom: "PCS");
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "RM-008", 10m, 20000m, uom: "PALLET"); // Unknown PALLET with no rule

        var result = _engine.Match(po, [gr], inv, [], _defaultTolerance);

        Assert.Equal(MatchStatus.Discrepancy, result.Status);
        Assert.Contains(result.Discrepancies, d => d.Type == DiscrepancyType.UomConversionError);
    }

    // Helpers
    private static PurchaseOrder CreatePo(string poNum, string suppCode, string sku, decimal qty, decimal price, string uom = "PCS", string currency = "VND")
    {
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = poNum,
            SupplierCode = suppCode,
            SupplierName = "Test Supplier",
            Currency = currency,
            Status = PurchaseOrderStatus.Open,
            Lines =
            [
                new PurchaseOrderLine
                {
                    Id = Guid.NewGuid(),
                    LineNumber = 1,
                    ItemSku = sku,
                    Description = "Test Item",
                    OrderedQuantity = qty,
                    UnitOfMeasure = uom,
                    UnitPrice = price,
                    TotalAmount = Math.Round(qty * price, 2, MidpointRounding.AwayFromZero)
                }
            ]
        };
        po.TotalAmount = po.Lines.Sum(l => l.TotalAmount);
        return po;
    }

    private static GoodsReceipt CreateGr(string grNum, string poNum, string suppCode, string sku, decimal qty, string uom = "PCS")
    {
        return new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            ReceiptNumber = grNum,
            PurchaseOrderNumber = poNum,
            SupplierCode = suppCode,
            ReceivedAt = DateTimeOffset.UtcNow,
            Lines =
            [
                new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    LineNumber = 1,
                    ItemSku = sku,
                    ReceivedQuantity = qty,
                    UnitOfMeasure = uom,
                    AllocatedQuantity = 0m
                }
            ]
        };
    }

    private static SupplierInvoice CreateInvoice(string invNum, string poNum, string suppCode, string sku, decimal qty, decimal price, string uom = "PCS", string currency = "VND")
    {
        var sub = Math.Round(qty * price, 2, MidpointRounding.AwayFromZero);
        return new SupplierInvoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = invNum,
            PurchaseOrderNumber = poNum,
            SupplierCode = suppCode,
            Currency = currency,
            SubTotal = sub,
            TotalAmount = sub,
            Status = InvoiceStatus.PendingReconciliation,
            Lines =
            [
                new SupplierInvoiceLine
                {
                    Id = Guid.NewGuid(),
                    LineNumber = 1,
                    ItemSku = sku,
                    InvoicedQuantity = qty,
                    UnitOfMeasure = uom,
                    UnitPrice = price,
                    LineTotal = sub
                }
            ]
        };
    }

    private sealed class FakeSupplierInvoiceRepository : ISupplierInvoiceRepository
    {
        private readonly List<SupplierInvoice> _list = [];
        public Task<SupplierInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_list.FirstOrDefault(i => i.Id == id));
        public Task<SupplierInvoice?> GetByNumberAndSupplierAsync(string invoiceNumber, string supplierCode, CancellationToken ct = default) =>
            Task.FromResult(_list.FirstOrDefault(i => i.InvoiceNumber == invoiceNumber && i.SupplierCode == supplierCode));
        public Task<IReadOnlyList<SupplierInvoice>> GetByPurchaseOrderNumberAsync(string poNumber, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SupplierInvoice>>(_list.Where(i => i.PurchaseOrderNumber == poNumber).ToList());
        public Task AddAsync(SupplierInvoice invoice, CancellationToken ct = default)
        {
            _list.Add(invoice);
            return Task.CompletedTask;
        }
        public Task UpdateAsync(SupplierInvoice invoice, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SupplierInvoice>> ListAsync(int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SupplierInvoice>>(_list.Take(limit).ToList());
    }

    private sealed class FakeReconciliationRepository : IReconciliationRepository
    {
        private readonly List<Discrepancy> _discrepancies = [];
        public Task AddBatchAsync(ReconciliationBatch batch, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ReconciliationBatch?> GetBatchByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ReconciliationBatch?>(null);
        public Task AddResultAsync(ReconciliationResult result, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ReconciliationResult?> GetResultByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ReconciliationResult?>(null);
        public Task<IReadOnlyList<Discrepancy>> GetDiscrepanciesAsync(DiscrepancyStatus? status = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Discrepancy>>(_discrepancies.ToList());
        public Task<Discrepancy?> GetDiscrepancyByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_discrepancies.FirstOrDefault(d => d.Id == id));
        public Task UpdateDiscrepancyAsync(Discrepancy discrepancy, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddReviewDecisionAsync(ReviewDecision decision, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddDiscrepancyAsync(Discrepancy d)
        {
            _discrepancies.Add(d);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditEventRepository : IAuditEventRepository
    {
        public List<AuditEvent> Events { get; } = [];
        public Task AddAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int limit = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>(Events.Take(limit).ToList());
    }
}
