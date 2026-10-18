namespace OpsBridge.Application.Reconciliation;

public sealed record TolerancePolicy(
    decimal PriceTolerancePercentage = 0.01m,    // 1.0% price tolerance by default
    decimal QuantityTolerancePercentage = 0.00m, // 0.0% quantity tolerance by default
    decimal QuantityToleranceAbsolute = 0.00m,   // absolute quantity variance allowance
    bool AllowPartialReceiptMatching = true
);
