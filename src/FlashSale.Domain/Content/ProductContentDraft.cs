namespace FlashSale.Domain.Content;

/// <summary>
/// AI product-content lifecycle (spec §8). Generated content can never skip
/// human review: the ONLY path to Published goes through Approved.
/// </summary>
public enum ProductContentStatus
{
    /// <summary>Placeholder state (no content yet).</summary>
    Draft,
    /// <summary>AI produced a validated draft.</summary>
    AiGenerated,
    /// <summary>Draft is waiting for a human decision (mandatory — spec §13).</summary>
    ReviewRequired,
    /// <summary>Human approved; publishing is now allowed.</summary>
    Approved,
    /// <summary>Human rejected (regeneration is allowed).</summary>
    Rejected,
    /// <summary>Applied to the product — terminal state.</summary>
    Published
}

/// <summary>
/// One AI-generated product-content draft (spec §8). Stores the raw model
/// output fields plus usage metadata so the demo can show model/latency/tokens
/// and review can be audited.
/// </summary>
public sealed class ProductContentDraft
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public ProductContentStatus Status { get; set; } = ProductContentStatus.Draft;

    public string ShortDescription { get; set; } = string.Empty;
    public string SeoDescription { get; set; } = string.Empty;
    public string KeywordsJson { get; set; } = "[]";
    public string SocialCaption { get; set; } = string.Empty;
    public string FaqJson { get; set; } = "[]";

    public string Model { get; set; } = "n/a";
    public long LatencyMs { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid CorrelationId { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}