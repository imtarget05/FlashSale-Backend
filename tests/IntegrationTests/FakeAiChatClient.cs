using FlashSale.Application.Assistant;

namespace FlashSale.IntegrationTests;

/// <summary>
/// Canned AI chat client for integration tests: the Ollama model is a local dev
/// dependency, so tests assert the PLUMBING (validation, guards, audit, event
/// flow) against fixed responses while the live smoke covers real inference.
/// </summary>
internal sealed class FakeAiChatClient : IAiChatClient
{
    public string Content { get; set; } = "{}";
    public int Calls { get; private set; }

    public Task<AiChatResponse> CompleteAsync(IReadOnlyList<ChatTurn> turns, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(new AiChatResponse(Content, "fake-model", 12, 8));
    }
}