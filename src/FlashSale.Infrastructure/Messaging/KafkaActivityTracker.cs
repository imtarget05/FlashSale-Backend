using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using FlashSale.Domain.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Messaging;

public class KafkaActivityTracker : IHostedService, IDisposable
{
    private readonly IProducer<Null, string> _producer;
    private readonly IConsumer<Null, string> _consumer;
    private readonly ILogger<KafkaActivityTracker> _logger;
    private const string TopicName = "user-activities";
    private CancellationTokenSource _cts;

    public KafkaActivityTracker(string bootstrapServers, ILogger<KafkaActivityTracker> logger)
    {
        _logger = logger;

        var producerConfig = new ProducerConfig { BootstrapServers = bootstrapServers };
        _producer = new ProducerBuilder<Null, string>(producerConfig).Build();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "activity-tracker-group",
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        _consumer = new ConsumerBuilder<Null, string>(consumerConfig).Build();
    }

    public async Task TrackActivityAsync(string action, string userId, string details)
    {
        var activity = new { Action = action, UserId = userId, Details = details, Timestamp = DateTimeOffset.UtcNow };
        var json = JsonSerializer.Serialize(activity);
        
        try 
        {
            await _producer.ProduceAsync(TopicName, new Message<Null, string> { Value = json });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track activity in Kafka");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _consumer.Subscribe(TopicName);

        Task.Run(() => ConsumeLoop(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private void ConsumeLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var consumeResult = _consumer.Consume(cancellationToken);
                _logger.LogInformation("Consumed activity from Kafka: {Activity}", consumeResult.Message.Value);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consuming from Kafka");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _producer?.Dispose();
        _consumer?.Dispose();
        _cts?.Dispose();
    }
}
