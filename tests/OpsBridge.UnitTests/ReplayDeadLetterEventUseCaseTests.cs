using OpsBridge.Application.InboxOutbox;
using OpsBridge.Application.UseCases;

namespace OpsBridge.UnitTests;

public sealed class ReplayDeadLetterEventUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_DelegatesReplayCommandToRepositoryPort()
    {
        var eventId = Guid.NewGuid();
        var repository = new FakeDeadLetterEventRepository();
        var useCase = new ReplayDeadLetterEventUseCase(repository);

        var result = await useCase.ExecuteAsync(eventId, "operator-17");

        Assert.Equal(eventId, repository.EventId);
        Assert.Equal("operator-17", repository.Actor);
        Assert.Equal(DeadLetterReplayStatus.Queued, result.Status);
    }

    private sealed class FakeDeadLetterEventRepository : IDeadLetterEventRepository
    {
        public Guid EventId { get; private set; }
        public string Actor { get; private set; } = string.Empty;

        public Task<IReadOnlyList<OpsBridge.Domain.DLQ.DeadLetterEvent>> GetRecentAsync(
            int limit,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OpsBridge.Domain.DLQ.DeadLetterEvent>>([]);

        public Task<DeadLetterReplayResult> QueueReplayAsync(
            Guid eventId,
            string actor,
            CancellationToken ct = default)
        {
            EventId = eventId;
            Actor = actor;
            return Task.FromResult(new DeadLetterReplayResult(DeadLetterReplayStatus.Queued, eventId));
        }
    }
}