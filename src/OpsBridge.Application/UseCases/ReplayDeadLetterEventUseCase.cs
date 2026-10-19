using OpsBridge.Application.InboxOutbox;

namespace OpsBridge.Application.UseCases;

public sealed class ReplayDeadLetterEventUseCase(IDeadLetterEventRepository repository)
{
    public Task<DeadLetterReplayResult> ExecuteAsync(
        Guid eventId,
        string actor,
        CancellationToken ct = default) =>
        repository.QueueReplayAsync(eventId, actor, ct);
}