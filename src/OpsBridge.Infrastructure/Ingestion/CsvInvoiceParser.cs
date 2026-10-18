using System.Globalization;
using OpsBridge.Domain.Reconciliation;

namespace OpsBridge.Infrastructure.Ingestion;

public static class CsvInvoiceParser
{
    public static List<SupplierInvoice> Parse(string csvContent)
    {
        var result = new Dictionary<string, SupplierInvoice>(StringComparer.OrdinalIgnoreCase);

        var lines = csvContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1) return [];

        // Header: InvoiceNumber,PurchaseOrderNumber,SupplierCode,SupplierName,Currency,LineNumber,ItemSku,Description,InvoicedQuantity,UnitOfMeasure,UnitPrice,TaxRate
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length < 12) continue;

            var invoiceNumber = parts[0].Trim();
            var poNumber = parts[1].Trim();
            var supplierCode = parts[2].Trim();
            var supplierName = parts[3].Trim();
            var currency = parts[4].Trim();
            var lineNumber = int.Parse(parts[5].Trim(), CultureInfo.InvariantCulture);
            var itemSku = parts[6].Trim();
            var description = parts[7].Trim();
            var invoicedQty = decimal.Parse(parts[8].Trim(), CultureInfo.InvariantCulture);
            var uom = parts[9].Trim();
            var unitPrice = decimal.Parse(parts[10].Trim(), CultureInfo.InvariantCulture);
            var taxRate = decimal.Parse(parts[11].Trim(), CultureInfo.InvariantCulture);

            var key = $"{supplierCode}:{invoiceNumber}";
            if (!result.TryGetValue(key, out var invoice))
            {
                invoice = new SupplierInvoice
                {
                    Id = Guid.NewGuid(),
                    InvoiceNumber = invoiceNumber,
                    PurchaseOrderNumber = poNumber,
                    SupplierCode = supplierCode,
                    SupplierName = supplierName,
                    Currency = currency,
                    InvoiceDate = DateTimeOffset.UtcNow,
                    Status = InvoiceStatus.PendingReconciliation,
                    SourceSystem = "CsvImport",
                    Lines = []
                };
                result[key] = invoice;
            }

            var lineSubTotal = Math.Round(invoicedQty * unitPrice, 2, MidpointRounding.AwayFromZero);
            var lineTax = Math.Round(lineSubTotal * taxRate, 2, MidpointRounding.AwayFromZero);
            var lineTotal = lineSubTotal + lineTax;

            invoice.Lines.Add(new SupplierInvoiceLine
            {
                Id = Guid.NewGuid(),
                SupplierInvoiceId = invoice.Id,
                LineNumber = lineNumber,
                ItemSku = itemSku,
                Description = description,
                InvoicedQuantity = invoicedQty,
                UnitOfMeasure = uom,
                UnitPrice = unitPrice,
                TaxRate = taxRate,
                LineTotal = lineTotal
            });

            invoice.SubTotal += lineSubTotal;
            invoice.TaxAmount += lineTax;
            invoice.TotalAmount += lineTotal;
        }

        return result.Values.ToList();
    }
}
