namespace FlashSale.Application.Assistant;

/// <summary>One chat turn handed to the model.</summary>
public sealed record ChatTurn(string Role, string Content);

/// <summary>Raw completion result including usage figures (spec §10 logging).</summary>
public sealed record AiChatResponse(string Content, string Model, long PromptTokens, long CompletionTokens);

/// <summary>
/// Port: one non-streaming chat completion against the local LLM
/// (Ollama, OpenAI-compatible endpoint). Implementation lives in Infrastructure;
/// timeout and bounded retry are owned by <see cref="ProductAssistantUseCase"/>.
/// </summary>
public interface IAiChatClient
{
    /// <summary>Throws on transport/HTTP failure — never returns a null response.</summary>
    Task<AiChatResponse> CompleteAsync(IReadOnlyList<ChatTurn> turns, CancellationToken ct = default);
}

/// <summary>
/// Port: per-user request throttle for the assistant (spec §10 "rate limit").
/// The backing store and its fail-open behaviour on outage are the
/// implementation's decision.
/// </summary>
public interface IAiRateLimiter
{
    /// <summary>True when this user may make one more request right now.</summary>
    Task<bool> TryAcquireAsync(Guid userId, CancellationToken ct = default);
}
