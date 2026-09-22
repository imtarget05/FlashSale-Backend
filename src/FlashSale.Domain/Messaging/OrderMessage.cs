namespace FlashSale.Domain.Messaging;

/// <summary>
/// A purchase order traveling from the API to the Worker (value object).
/// Attempt supports the at-least-once retry model: a failed message is
/// re-enqueued with Attempt+1 until MaxAttempts, then dead-lettered.
/// </summary>
/// <remarks>
/// <see cref="UserId"/> is carried through the queue so the Worker persists the
/// owner on the order row (ADR-013 §6). It is optional: anonymous orders keep
/// working exactly as before.
/// </remarks>
public sealed record OrderMessage(
    int ProductId,
    int Quantity,
    string IdempotencyKey,
    DateTimeOffset CreatedAt,
    int Attempt = 0,
    Guid? UserId = null)
{
    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this);

    public static OrderMessage FromJson(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<OrderMessage>(json)
        ?? throw new InvalidOperationException("Unreadable OrderMessage payload.");
}
