using System.Globalization;
using OpsBridge.Domain.Reconciliation;

namespace OpsBridge.Infrastructure.Ingestion;

public static class CsvPurchaseOrderParser
{
    public static List<PurchaseOrder> Parse(string csvContent)
    {
        var result = new Dictionary<string, PurchaseOrder>(StringComparer.OrdinalIgnoreCase);

        var lines = csvContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1) return [];

        // Header: OrderNumber,SupplierCode,SupplierName,Currency,LineNumber,ItemSku,Description,OrderedQuantity,UnitOfMeasure,UnitPrice
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length < 10) continue;

            var orderNumber = parts[0].Trim();
            var supplierCode = parts[1].Trim();
            var supplierName = parts[2].Trim();
            var currency = parts[3].Trim();
            var lineNumber = int.Parse(parts[4].Trim(), CultureInfo.InvariantCulture);
            var itemSku = parts[5].Trim();
            var description = parts[6].Trim();
            var orderedQty = decimal.Parse(parts[7].Trim(), CultureInfo.InvariantCulture);
            var uom = parts[8].Trim();
            var unitPrice = decimal.Parse(parts[9].Trim(), CultureInfo.InvariantCulture);

            if (!result.TryGetValue(orderNumber, out var po))
            {
                po = new PurchaseOrder
                {
                    Id = Guid.NewGuid(),
                    OrderNumber = orderNumber,
                    SupplierCode = supplierCode,
                    SupplierName = supplierName,
                    Currency = currency,
                    Status = PurchaseOrderStatus.Open,
                    OrderedAt = DateTimeOffset.UtcNow,
                    SourceSystem = "CsvImport",
                    Lines = []
                };
                result[orderNumber] = po;
            }

            var lineTotal = Math.Round(orderedQty * unitPrice, 2, MidpointRounding.AwayFromZero);
            po.Lines.Add(new PurchaseOrderLine
            {
                Id = Guid.NewGuid(),
                PurchaseOrderId = po.Id,
                LineNumber = lineNumber,
                ItemSku = itemSku,
                Description = description,
                OrderedQuantity = orderedQty,
                UnitOfMeasure = uom,
                UnitPrice = unitPrice,
                TotalAmount = lineTotal
            });
            po.TotalAmount += lineTotal;
        }

        return result.Values.ToList();
    }
}
