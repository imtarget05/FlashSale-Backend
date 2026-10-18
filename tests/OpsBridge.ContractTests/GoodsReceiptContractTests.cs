using OpsBridge.Domain.Contracts;
using OpsBridge.Infrastructure.Contracts;

namespace OpsBridge.ContractTests;

public class GoodsReceiptContractTests
{
    private readonly JsonContractValidator _validator = new();

    [Fact]
    public void Validate_ValidContractSample_Succeeds()
    {
        var json = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "valid-goods-receipt-v1.json"));

        var result = _validator.ValidateGoodsReceiptConfirmedV1(json);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Contract);

        var contract = result.Contract;
        Assert.Equal("GoodsReceiptConfirmed", contract.EventType);
        Assert.Equal("1.0", contract.SchemaVersion);
        Assert.Equal("factoryflow.manufacturing.warehouse", contract.Source);
        Assert.Equal("GR-2026-10042", contract.Data.ReceiptNumber);
        Assert.Equal("SUP-ACME-01", contract.Data.SupplierCode);
        Assert.Single(contract.Data.Items);
        Assert.Equal("RM-ALUM-SHEET-02", contract.Data.Items[0].ItemCode);
        Assert.Equal(150.0m, contract.Data.Items[0].QuantityReceived);
        Assert.Equal("Accepted", contract.Data.Items[0].QcStatus);
    }

    [Fact]
    public void Validate_MissingRequiredFields_FailsWithDescriptiveError()
    {
        var json = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "invalid-goods-receipt-v1-missing-fields.json"));

        var result = _validator.ValidateGoodsReceiptConfirmedV1(json);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Validate_UnsupportedSchemaVersion_Fails()
    {
        var json = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "invalid-goods-receipt-v1-bad-schema-version.json"));

        var result = _validator.ValidateGoodsReceiptConfirmedV1(json);

        Assert.False(result.IsValid);
        Assert.Contains("Unsupported 'schemaVersion'", result.ErrorMessage);
    }

    [Theory]
    [InlineData("InvalidStatus")]
    [InlineData("Pending")]
    [InlineData("Done")]
    public void Validate_InvalidQcStatus_Fails(string invalidStatus)
    {
        var validJson = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "valid-goods-receipt-v1.json"));
        var modifiedJson = validJson.Replace("\"Accepted\"", $"\"{invalidStatus}\"");

        var result = _validator.ValidateGoodsReceiptConfirmedV1(modifiedJson);

        Assert.False(result.IsValid);
        Assert.Contains("invalid 'qcStatus'", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_PayloadWithPurchaseOrderNoAlias_Succeeds()
    {
        var validJson = File.ReadAllText(Path.Combine("..", "..", "..", "..", "fixtures", "valid-goods-receipt-v1.json"));
        // Replace purchaseOrderNumber with purchaseOrderNo
        var modifiedJson = validJson.Replace("\"purchaseOrderNumber\"", "\"purchaseOrderNo\"");

        var result = _validator.ValidateGoodsReceiptConfirmedV1(modifiedJson);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.NotNull(result.Contract);
        Assert.Equal("PO-2026-0891", result.Contract.Data.PurchaseOrderNumber);
    }
}
