namespace FlashSale.Domain.Messaging;

/// <summary>
/// A purchase order traveling from the API to the Worker (value object).
/// Attempt supports the at-least-once retry model: a failed message is
/// re-enqueued with Attempt+1 until MaxAttempts, then dead-lettered.
/// </summary>
public sealed record OrderMessage(
    int ProductId,
    int Quantity,
    string IdempotencyKey,
    DateTimeOffset CreatedAt,
    int Attempt = 0)
{
    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this);

    public static OrderMessage FromJson(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<OrderMessage>(json)
        ?? throw new InvalidOperationException("Unreadable OrderMessage payload.");
}
