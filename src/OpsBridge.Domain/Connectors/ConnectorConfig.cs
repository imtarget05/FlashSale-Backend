namespace OpsBridge.Domain.Connectors;

public enum ConnectorType
{
    FactoryFlowWebhook = 1,
    FactoryFlowRest = 2,
    CsvImport = 3,
    GoogleSheets = 4
}

public class ConnectorConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ConnectorType Type { get; set; }
    public string Secret { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
