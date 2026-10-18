using System.Text.Json;
using OpsBridge.Application.Contracts;
using OpsBridge.Domain.Contracts;

namespace OpsBridge.Infrastructure.Contracts;

public sealed class JsonContractValidator : IContractValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ContractValidationResult ValidateGoodsReceiptConfirmedV1(string jsonPayload)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            return ContractValidationResult.Failure("Payload cannot be empty.");
        }

        GoodsReceiptConfirmedV1? contract;
        try
        {
            contract = JsonSerializer.Deserialize<GoodsReceiptConfirmedV1>(jsonPayload, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ContractValidationResult.Failure($"Malformed JSON payload: {ex.Message}");
        }

        if (contract == null)
        {
            return ContractValidationResult.Failure("Failed to deserialize event payload.");
        }

        // Schema validation checks
        if (contract.EventId == Guid.Empty)
        {
            return ContractValidationResult.Failure("Missing or empty 'eventId'.");
        }

        if (!string.Equals(contract.EventType, GoodsReceiptConfirmedV1.ExpectedEventType, StringComparison.Ordinal))
        {
            return ContractValidationResult.Failure($"Invalid 'eventType'. Expected '{GoodsReceiptConfirmedV1.ExpectedEventType}', got '{contract.EventType}'.");
        }

        if (!string.Equals(contract.SchemaVersion, GoodsReceiptConfirmedV1.ExpectedSchemaVersion, StringComparison.Ordinal))
        {
            return ContractValidationResult.Failure($"Unsupported 'schemaVersion'. Expected '{GoodsReceiptConfirmedV1.ExpectedSchemaVersion}', got '{contract.SchemaVersion}'.");
        }

        if (!string.Equals(contract.Source, GoodsReceiptConfirmedV1.ExpectedSource, StringComparison.Ordinal))
        {
            return ContractValidationResult.Failure($"Unexpected 'source'. Expected '{GoodsReceiptConfirmedV1.ExpectedSource}', got '{contract.Source}'.");
        }

        if (contract.CorrelationId == Guid.Empty)
        {
            return ContractValidationResult.Failure("Missing or empty 'correlationId'.");
        }

        if (contract.Data == null)
        {
            return ContractValidationResult.Failure("Missing required 'data' object.");
        }

        var data = contract.Data;
        if (data.ReceiptId == Guid.Empty)
        {
            return ContractValidationResult.Failure("Missing or empty 'data.receiptId'.");
        }

        if (string.IsNullOrWhiteSpace(data.ReceiptNumber))
        {
            return ContractValidationResult.Failure("Missing required 'data.receiptNumber'.");
        }

        if (string.IsNullOrWhiteSpace(data.PurchaseOrderNumber))
        {
            return ContractValidationResult.Failure("Missing required 'data.purchaseOrderNumber'.");
        }

        if (string.IsNullOrWhiteSpace(data.SupplierCode))
        {
            return ContractValidationResult.Failure("Missing required 'data.supplierCode'.");
        }

        if (string.IsNullOrWhiteSpace(data.WarehouseCode))
        {
            return ContractValidationResult.Failure("Missing required 'data.warehouseCode'.");
        }

        if (data.Items == null || data.Items.Count == 0)
        {
            return ContractValidationResult.Failure("The 'data.items' list must contain at least one item.");
        }

        for (int i = 0; i < data.Items.Count; i++)
        {
            var item = data.Items[i];
            if (string.IsNullOrWhiteSpace(item.ItemCode))
            {
                return ContractValidationResult.Failure($"Item at index {i} is missing 'itemCode'.");
            }
            if (item.PurchaseOrderLineNumber < 1)
            {
                return ContractValidationResult.Failure($"Item '{item.ItemCode}' has invalid 'purchaseOrderLineNumber': must be >= 1.");
            }
            if (item.QuantityReceived <= 0)
            {
                return ContractValidationResult.Failure($"Item '{item.ItemCode}' has invalid 'quantityReceived': must be greater than zero.");
            }
            if (item.QuantityRejected < 0)
            {
                return ContractValidationResult.Failure($"Item '{item.ItemCode}' has invalid 'quantityRejected': cannot be negative.");
            }
            if (string.IsNullOrWhiteSpace(item.UnitOfMeasure))
            {
                return ContractValidationResult.Failure($"Item '{item.ItemCode}' is missing 'unitOfMeasure'.");
            }
            if (string.IsNullOrWhiteSpace(item.LotNumber))
            {
                return ContractValidationResult.Failure($"Item '{item.ItemCode}' is missing 'lotNumber'.");
            }

            var validQcStatuses = new[] { "Accepted", "Rejected", "Quarantine" };
            if (!validQcStatuses.Contains(item.QcStatus, StringComparer.OrdinalIgnoreCase))
            {
                return ContractValidationResult.Failure($"Item '{item.ItemCode}' has invalid 'qcStatus': '{item.QcStatus}'. Allowed values: Accepted, Rejected, Quarantine.");
            }
        }

        return ContractValidationResult.Success(contract);
    }
}
