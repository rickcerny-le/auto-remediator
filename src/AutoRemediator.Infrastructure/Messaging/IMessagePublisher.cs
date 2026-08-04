using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace AutoRemediator.Infrastructure.Messaging;

/// <summary>
/// Publishes messages to a named Service Bus queue.
/// </summary>
public interface IMessagePublisher
{
    Task PublishAsync<T>(string queue, T message, CancellationToken cancellationToken = default);
}

internal sealed class ServiceBusMessagePublisher(ServiceBusClient client) : IMessagePublisher
{
    public async Task PublishAsync<T>(string queue, T message, CancellationToken cancellationToken = default)
    {
        var sender = client.CreateSender(queue);
        var body = JsonSerializer.Serialize(message);
        await sender.SendMessageAsync(new ServiceBusMessage(body) { ContentType = "application/json" }, cancellationToken);
    }
}
