using FlashSale.Application.Assistant;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlashSale.Infrastructure.Ai;

public sealed class OllamaOptions
{
    public const string SectionName = "Ai:Ollama";

    /// <summary>OpenAI-compatible base URL (Ollama serves /v1).</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";

    public string Model { get; set; } = "qwen3:4b";

    /// <summary>
    /// Ollama does not validate API keys, but OpenAI-compatible SDKs/clients
    /// always send one — a dummy default satisfies the wire format (spec note).
    /// </summary>
    public string ApiKey { get; set; } = "ollama";
}

/// <summary>
/// Infrastructure adapter: one non-streaming chat completion against the local
/// Ollama server through its OpenAI-compatible endpoint (<c>POST /chat/completions</c>,
/// bearer auth from <see cref="OllamaOptions.ApiKey"/>). Transport/HTTP failures
/// throw; timeout + bounded retry are owned by the use case.
/// </summary>
public sealed class OllamaChatClient(
    HttpClient http,
    OllamaOptions options,
    ILogger<OllamaChatClient> logger) : IAiChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AiChatResponse> CompleteAsync(IReadOnlyList<ChatTurn> turns, CancellationToken ct = default)
    {
        var payload = new
        {
            model = options.Model,
            stream = false,
            messages = turns.Select(t => new { role = t.Role, content = t.Content }).ToArray(),
            options = new { temperature = 0.2 }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("chat/completions", content, ct);

        // Non-2xx (model not pulled, server restarting, …) → HttpRequestException:
        // the use case logs it and applies its bounded retry.
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        var parsed = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(body, JsonOptions, ct)
            ?? throw new InvalidOperationException("Ollama returned an empty body.");

        var message = parsed.Choices is [{ } first] ? first.Message?.Content : null;
        if (string.IsNullOrWhiteSpace(message))
            throw new InvalidOperationException("Ollama returned no choices[0].message.content.");

        var model = string.IsNullOrWhiteSpace(parsed.Model) ? options.Model : parsed.Model;
        logger.LogDebug("Ollama completion model={Model} promptTokens={Prompt} completionTokens={Completion}.",
            model, parsed.Usage?.PromptTokens ?? 0, parsed.Usage?.CompletionTokens ?? 0);

        return new AiChatResponse(
            message,
            model,
            parsed.Usage?.PromptTokens ?? 0,
            parsed.Usage?.CompletionTokens ?? 0);
    }

    /// <summary>OpenAI chat-completion response shape (camelCase fields, snake_case usage).</summary>
    private sealed record ChatCompletionResponse(string? Model, ChatChoice[]? Choices, ChatUsage? Usage);
    private sealed record ChatChoice(ChatMessage? Message);
    private sealed record ChatMessage(string? Content);

    private sealed record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] long PromptTokens,
        [property: JsonPropertyName("completion_tokens")] long CompletionTokens);
}
