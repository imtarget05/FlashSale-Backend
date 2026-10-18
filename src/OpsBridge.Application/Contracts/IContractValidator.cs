using OpsBridge.Domain.Contracts;

namespace OpsBridge.Application.Contracts;

public sealed record ContractValidationResult(
    bool IsValid,
    string? ErrorMessage,
    GoodsReceiptConfirmedV1? Contract
)
{
    public static ContractValidationResult Success(GoodsReceiptConfirmedV1 contract) =>
        new(true, null, contract);

    public static ContractValidationResult Failure(string errorMessage) =>
        new(false, errorMessage, null);
}

public interface IContractValidator
{
    ContractValidationResult ValidateGoodsReceiptConfirmedV1(string jsonPayload);
}
