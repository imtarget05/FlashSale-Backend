using Microsoft.Extensions.Logging.Abstractions;
using OpsBridge.Application.Common;
using OpsBridge.Application.Reconciliation;
using OpsBridge.Application.UseCases;
using OpsBridge.Domain;
using OpsBridge.Domain.Reconciliation;

namespace OpsBridge.UnitTests;

public class SkuAliasMappingTests
{
    private readonly ThreeWayMatchingEngine _engine = new();
    private readonly TolerancePolicy _defaultTolerance = new(
        PriceTolerancePercentage: 0.01m,
        QuantityTolerancePercentage: 0.00m,
        QuantityToleranceAbsolute: 0.00m
    );

    [Fact]
    public void Match_SkuAliasMapping_ResolvesToPoSku()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m);
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 100m, "PCS");
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "SUPPLIER-SKU-99", 100m, 20000m, "PCS");

        var rules = new[]
        {
            new MappingRule
            {
                RuleType = MappingRuleType.SkuAlias,
                SourceValue = "SUPPLIER-SKU-99",
                TargetValue = "RM-008",
                SupplierCode = "SUPP-01",
                IsActive = true
            }
        };

        var result = _engine.Match(po, [gr], inv, rules, _defaultTolerance);

        Assert.Equal(MatchStatus.Matched, result.Status);
        Assert.Empty(result.Discrepancies);
        Assert.Single(result.Allocations);
    }

    [Fact]
    public void Match_UnknownSkuAlias_ReturnsDiscrepancy()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m);
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 100m, "PCS");
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "UNKNOWN-SKU", 100m, 20000m, "PCS");

        var rules = Array.Empty<MappingRule>();

        var result = _engine.Match(po, [gr], inv, rules, _defaultTolerance);

        Assert.True(result.Status != MatchStatus.Matched, "Expected discrepancy status when SKU alias not resolved");
    }

    [Fact]
    public void Match_InactiveSkuAliasRule_ReturnsDiscrepancy()
    {
        var po = CreatePo("PO-1001", "SUPP-01", "RM-008", 100m, 20000m);
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-01", "RM-008", 100m, "PCS");
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-01", "SUPPLIER-SKU-99", 100m, 20000m, "PCS");

        var rules = new[]
        {
            new MappingRule
            {
                RuleType = MappingRuleType.SkuAlias,
                SourceValue = "SUPPLIER-SKU-99",
                TargetValue = "RM-008",
                SupplierCode = "SUPP-01",
                IsActive = false
            }
        };

        var result = _engine.Match(po, [gr], inv, rules, _defaultTolerance);

        Assert.True(result.Status != MatchStatus.Matched, "Expected discrepancy status when SkuAlias rule is inactive");
    }

    [Fact]
    public void Match_SkuAliasRule_ScopedToWrongSupplier_ReturnsDiscrepancy()
    {
        var po = CreatePo("PO-1001", "SUPP-02", "RM-008", 100m, 20000m);
        var gr = CreateGr("GR-001", "PO-1001", "SUPP-02", "RM-008", 100m, "PCS");
        var inv = CreateInvoice("INV-001", "PO-1001", "SUPP-02", "SUPPLIER-SKU-99", 100m, 20000m, "PCS");

        var rules = new[]
        {
            new MappingRule
            {
                RuleType = MappingRuleType.SkuAlias,
                SourceValue = "SUPPLIER-SKU-99",
                TargetValue = "RM-008",
                SupplierCode = "SUPP-01",
                IsActive = true
            }
        };

        var result = _engine.Match(po, [gr], inv, rules, _defaultTolerance);

        Assert.True(result.Status != MatchStatus.Matched, "Expected discrepancy status when SkuAlias rule is scoped to wrong supplier");
    }

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

    private static SupplierInvoice CreateInvoice(string invNum, string poNum, string suppCode, string sku, decimal qty, decimal price, string uom = "PCS", InvoiceStatus status = InvoiceStatus.PendingReconciliation)
    {
        var invoice = new SupplierInvoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = invNum,
            PurchaseOrderNumber = poNum,
            SupplierCode = suppCode,
            SupplierName = "Test Supplier",
            InvoiceDate = DateTimeOffset.UtcNow,
            Currency = "VND",
            SubTotal = Math.Round(qty * price, 2, MidpointRounding.AwayFromZero),
            TaxAmount = 0m,
            DiscountAmount = 0m,
            AdditionalCharges = 0m,
            TotalAmount = Math.Round(qty * price, 2, MidpointRounding.AwayFromZero),
            Status = status,
            SourceSystem = "ManualImport",
            Version = 1,
            Lines =
            [
                new SupplierInvoiceLine
                {
                    Id = Guid.NewGuid(),
                    LineNumber = 1,
                    ItemSku = sku,
                    Description = "Test Item",
                    InvoicedQuantity = qty,
                    UnitOfMeasure = uom,
                    UnitPrice = price,
                    LineTotal = Math.Round(qty * price, 2, MidpointRounding.AwayFromZero)
                }
            ]
        };
        return invoice;
    }
}
