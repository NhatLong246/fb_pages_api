using Confluent.Kafka;
using CoreService.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CoreService.Services
{
    public interface IFacebookActionCommandPublisher
    {
        Task PublishAsync(FacebookActionCommand command, CancellationToken ct);
    }

    public sealed class FacebookActionCommandPublisher : IFacebookActionCommandPublisher, IDisposable
    {
        private readonly IProducer<string, string> _producer;
        private readonly KafkaConsumerOptions _options;
        private readonly ILogger<FacebookActionCommandPublisher> _logger;

        public FacebookActionCommandPublisher(
            IOptions<KafkaConsumerOptions> options,
            ILogger<FacebookActionCommandPublisher> logger)
        {
            _options = options.Value;
            _logger = logger;
            _producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                EnableIdempotence = true,
                Acks = Acks.All
            }).Build();
        }

        public async Task PublishAsync(FacebookActionCommand command, CancellationToken ct)
        {
            await _producer.ProduceAsync(
                _options.CommandTopic,
                new Message<string, string>
                {
                    Key = command.CommandId,
                    Value = JsonSerializer.Serialize(command)
                },
                ct);

            _logger.LogInformation(
                "[COMMAND] Published. CommandId={CommandId} EventId={EventId} Action={Action} Topic={Topic}",
                command.CommandId,
                command.EventId,
                command.Action,
                _options.CommandTopic);
        }

        public void Dispose()
        {
            _producer.Flush(TimeSpan.FromSeconds(5));
            _producer.Dispose();
        }
    }
}
