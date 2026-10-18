using OpsBridge.Application.Reconciliation;
using OpsBridge.Domain.Reconciliation;

namespace OpsBridge.Application.UseCases;

public sealed class ThreeWayMatchingEngine
{
    public ReconciliationResult Match(
        PurchaseOrder? po,
        IReadOnlyList<GoodsReceipt> goodsReceipts,
        SupplierInvoice invoice,
        IReadOnlyList<MappingRule> mappingRules,
        TolerancePolicy tolerance)
    {
        var result = new ReconciliationResult
        {
            Id = Guid.NewGuid(),
            PurchaseOrderNumber = invoice.PurchaseOrderNumber,
            InvoiceNumber = invoice.InvoiceNumber,
            CreatedAt = DateTimeOffset.UtcNow,
            TotalQuantityInvoiced = invoice.Lines.Sum(l => l.InvoicedQuantity),
            TotalQuantityOrdered = po?.Lines.Sum(l => l.OrderedQuantity) ?? 0m,
            TotalQuantityReceived = goodsReceipts.SelectMany(g => g.Lines).Sum(l => l.ReceivedQuantity)
        };

        // 1. Pre-validation: Missing PO
        if (po == null)
        {
            result.Status = MatchStatus.Discrepancy;
            result.Discrepancies.Add(new Discrepancy
            {
                ReconciliationResultId = result.Id,
                Type = DiscrepancyType.MissingPurchaseOrder,
                Severity = DiscrepancySeverity.Error,
                ExpectedValue = invoice.PurchaseOrderNumber,
                ActualValue = "None",
                Details = $"Purchase order '{invoice.PurchaseOrderNumber}' referenced by invoice was not found."
            });
            return result;
        }

        // 2. Pre-validation: Currency Mismatch
        if (!string.Equals(po.Currency, invoice.Currency, StringComparison.OrdinalIgnoreCase))
        {
            result.Status = MatchStatus.Discrepancy;
            result.Discrepancies.Add(new Discrepancy
            {
                ReconciliationResultId = result.Id,
                Type = DiscrepancyType.CurrencyMismatch,
                Severity = DiscrepancySeverity.Error,
                ExpectedValue = po.Currency,
                ActualValue = invoice.Currency,
                Details = $"Currency mismatch: PO uses '{po.Currency}', but invoice uses '{invoice.Currency}'. Single-currency rule requires identical currency."
            });
        }

        // 2b. Pre-validation: Supplier Mismatch
        if (!string.IsNullOrWhiteSpace(po.SupplierCode) &&
            !string.IsNullOrWhiteSpace(invoice.SupplierCode) &&
            !string.Equals(po.SupplierCode, invoice.SupplierCode, StringComparison.OrdinalIgnoreCase))
        {
            result.Status = MatchStatus.Discrepancy;
            result.Discrepancies.Add(new Discrepancy
            {
                ReconciliationResultId = result.Id,
                Type = DiscrepancyType.SupplierMismatch,
                Severity = DiscrepancySeverity.Error,
                ExpectedValue = po.SupplierCode,
                ActualValue = invoice.SupplierCode,
                Details = $"Supplier mismatch: PO '{po.OrderNumber}' belongs to supplier '{po.SupplierCode}', but invoice '{invoice.InvoiceNumber}' was issued by '{invoice.SupplierCode}'."
            });
        }

        // 3. Pre-validation: Missing Goods Receipts
        var allReceiptLines = goodsReceipts
            .SelectMany(g => g.Lines)
            .ToList();

        if (allReceiptLines.Count == 0)
        {
            result.Status = MatchStatus.Discrepancy;
            result.Discrepancies.Add(new Discrepancy
            {
                ReconciliationResultId = result.Id,
                Type = DiscrepancyType.MissingGoodsReceipt,
                Severity = DiscrepancySeverity.Error,
                ExpectedValue = po.OrderNumber,
                ActualValue = "0 Receipts",
                Details = $"No goods receipts found for purchase order '{po.OrderNumber}'."
            });
        }

        // 4. Line Item Matching
        var plannedAllocations = new List<(GoodsReceiptLine Line, decimal Quantity, decimal UnitPrice, Guid InvoiceLineId)>();

        foreach (var invLine in invoice.Lines)
        {
            // Normalize SKU via mapping rules if alias exists
            var targetSku = ResolveSku(invLine.ItemSku, invoice.SupplierCode, mappingRules);

            // Find matching PO Line
            var poLine = po.Lines.FirstOrDefault(p =>
                string.Equals(p.ItemSku, targetSku, StringComparison.OrdinalIgnoreCase));

            if (poLine == null)
            {
                result.Discrepancies.Add(new Discrepancy
                {
                    ReconciliationResultId = result.Id,
                    Type = DiscrepancyType.UnmatchedInvoiceLine,
                    Severity = DiscrepancySeverity.Error,
                    ItemSku = invLine.ItemSku,
                    ExpectedValue = "PO Line Item",
                    ActualValue = invLine.ItemSku,
                    Details = $"Item SKU '{invLine.ItemSku}' on invoice line {invLine.LineNumber} does not match any line on PO '{po.OrderNumber}'."
                });
                continue;
            }

            // Normalize UOM
            var (normalizedQty, normalizedPrice, uomError) = NormalizeUom(
                invLine.InvoicedQuantity,
                invLine.UnitPrice,
                invLine.UnitOfMeasure,
                poLine.UnitOfMeasure,
                targetSku,
                mappingRules);

            if (uomError != null)
            {
                result.Discrepancies.Add(new Discrepancy
                {
                    ReconciliationResultId = result.Id,
                    Type = DiscrepancyType.UomConversionError,
                    Severity = DiscrepancySeverity.Error,
                    ItemSku = targetSku,
                    ExpectedValue = poLine.UnitOfMeasure,
                    ActualValue = invLine.UnitOfMeasure,
                    Details = uomError
                });
                continue;
            }

            // Check Unit Price against Tolerance
            var priceDiff = Math.Abs(normalizedPrice - poLine.UnitPrice);
            var maxAllowedPriceDiff = poLine.UnitPrice * tolerance.PriceTolerancePercentage;

            if (priceDiff > maxAllowedPriceDiff)
            {
                var priceVarianceAmount = Math.Round((normalizedPrice - poLine.UnitPrice) * normalizedQty, 2, MidpointRounding.AwayFromZero);
                result.TotalAmountVariance += priceVarianceAmount;

                result.Discrepancies.Add(new Discrepancy
                {
                    ReconciliationResultId = result.Id,
                    Type = DiscrepancyType.PriceMismatch,
                    Severity = DiscrepancySeverity.Error,
                    ItemSku = targetSku,
                    ExpectedValue = poLine.UnitPrice.ToString("F2"),
                    ActualValue = normalizedPrice.ToString("F2"),
                    VarianceAmount = priceVarianceAmount,
                    Details = $"Unit price '{normalizedPrice:F2}' exceeds PO price '{poLine.UnitPrice:F2}' beyond allowed tolerance ({tolerance.PriceTolerancePercentage * 100}%)."
                });
            }

            // Check Quantity against Available Received Goods across all receipts
            var candidateGrLines = allReceiptLines
                .Where(g => string.Equals(g.ItemSku, targetSku, StringComparison.OrdinalIgnoreCase) && g.AvailableQuantity > 0)
                .OrderBy(g => g.LineNumber)
                .ToList();

            var totalAvailableQty = candidateGrLines.Sum(g => g.AvailableQuantity);
            var qtyDiff = normalizedQty - totalAvailableQty;
            var maxAllowedQtyDiff = Math.Max(tolerance.QuantityToleranceAbsolute, normalizedQty * tolerance.QuantityTolerancePercentage);

            if (qtyDiff > maxAllowedQtyDiff)
            {
                result.Discrepancies.Add(new Discrepancy
                {
                    ReconciliationResultId = result.Id,
                    Type = DiscrepancyType.QuantityMismatch,
                    Severity = DiscrepancySeverity.Error,
                    ItemSku = targetSku,
                    ExpectedValue = totalAvailableQty.ToString("F4"),
                    ActualValue = normalizedQty.ToString("F4"),
                    VarianceAmount = qtyDiff,
                    Details = $"Invoiced quantity '{normalizedQty}' exceeds available received quantity '{totalAvailableQty}' beyond tolerance."
                });
            }
            else
            {
                // Plan allocation greedily across candidate receipt lines
                var remainingToAllocate = normalizedQty;
                foreach (var grLine in candidateGrLines)
                {
                    if (remainingToAllocate <= 0) break;

                    var allocate = Math.Min(remainingToAllocate, grLine.AvailableQuantity);
                    plannedAllocations.Add((grLine, allocate, normalizedPrice, invLine.Id));
                    remainingToAllocate -= allocate;
                }
            }
        }

        // Populate allocations on result
        foreach (var (line, qty, price, invLineId) in plannedAllocations)
        {
            result.Allocations.Add(new ReconciliationAllocation
            {
                Id = Guid.NewGuid(),
                ReconciliationResultId = result.Id,
                GoodsReceiptLineId = line.Id,
                InvoiceLineId = invLineId,
                ItemSku = line.ItemSku,
                AllocatedQuantity = qty,
                UnitPrice = price,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        // 5. Final Evaluation
        if (result.Discrepancies.Count > 0)
        {
            result.Status = MatchStatus.Discrepancy;
        }
        else
        {
            result.Status = MatchStatus.Matched;

            // In-memory allocation for callers using non-persisted domain entities
            foreach (var (line, qty, _, _) in plannedAllocations)
            {
                line.AllocatedQuantity += qty;
            }
        }

        return result;
    }

    private static string ResolveSku(string sourceSku, string supplierCode, IReadOnlyList<MappingRule> rules)
    {
        var rule = rules.FirstOrDefault(r =>
            r.IsActive
            && r.RuleType == MappingRuleType.SkuAlias
            && string.Equals(r.SourceValue, sourceSku, StringComparison.OrdinalIgnoreCase)
            && (r.SupplierCode == null || string.Equals(r.SupplierCode, supplierCode, StringComparison.OrdinalIgnoreCase)));

        return rule != null ? rule.TargetValue : sourceSku;
    }

    private static (decimal NormalizedQty, decimal NormalizedPrice, string? Error) NormalizeUom(
        decimal qty,
        decimal price,
        string sourceUom,
        string targetUom,
        string sku,
        IReadOnlyList<MappingRule> rules)
    {
        if (string.Equals(sourceUom, targetUom, StringComparison.OrdinalIgnoreCase))
        {
            return (qty, price, null);
        }

        var rule = rules.FirstOrDefault(r =>
            r.IsActive
            && r.RuleType == MappingRuleType.UomConversion
            && string.Equals(r.SourceValue, sourceUom, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.TargetValue, targetUom, StringComparison.OrdinalIgnoreCase));

        if (rule == null)
        {
            return (qty, price, $"No active UOM conversion rule found from '{sourceUom}' to '{targetUom}' for item '{sku}'.");
        }

        if (rule.ConversionFactor <= 0)
        {
            return (qty, price, $"Invalid UOM conversion factor '{rule.ConversionFactor}' from '{sourceUom}' to '{targetUom}'.");
        }

        var normalizedQty = Math.Round(qty * rule.ConversionFactor, 4, MidpointRounding.AwayFromZero);
        var normalizedPrice = Math.Round(price / rule.ConversionFactor, 4, MidpointRounding.AwayFromZero);
        return (normalizedQty, normalizedPrice, null);
    }
}
