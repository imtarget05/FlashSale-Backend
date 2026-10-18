namespace OpsBridge.Domain.Reconciliation;

public enum MappingRuleType
{
    SkuAlias,
    UomConversion
}

public sealed class MappingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MappingRuleType RuleType { get; set; }
    public string SourceValue { get; set; } = string.Empty; // e.g. "BOX" or "SUPPLIER-SKU-99"
    public string TargetValue { get; set; } = string.Empty; // e.g. "PCS" or "RM-008"
    public decimal ConversionFactor { get; set; } = 1.0m;   // 1 BOX = 10 PCS => factor 10.0
    public string? SupplierCode { get; set; }               // null applies globally or specific supplier
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
