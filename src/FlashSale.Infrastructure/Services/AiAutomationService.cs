namespace FlashSale.Infrastructure.Services;

public sealed class AiAutomationService
{
    public Task<string> GenerateAsync(string prompt, CancellationToken ct = default) => Task.FromResult("ai-stub");
}
