using WebhookService.Models;

namespace WebhookService.Services;

public interface IKafkaEventPublisher
{
    Task PublishAsync(IReadOnlyList<NormalizedFacebookEvent> events, CancellationToken cancellationToken = default);
}
