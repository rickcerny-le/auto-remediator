using Azure.Messaging.ServiceBus;

namespace AutoRemediator.Infrastructure.Messaging;

/// <summary>
/// Consumes messages from a named Service Bus queue, invoking <paramref name="handler"/>
/// for each message body until the supplied token is cancelled.
/// </summary>
public interface IMessageConsumer
{
    Task RunAsync(string queue, Func<string, CancellationToken, Task> handler, CancellationToken cancellationToken);
}

internal sealed class ServiceBusMessageConsumer(ServiceBusClient client) : IMessageConsumer
{
    public async Task RunAsync(string queue, Func<string, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        await using var processor = client.CreateProcessor(queue, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1,
        });

        processor.ProcessMessageAsync += async args =>
        {
            await handler(args.Message.Body.ToString(), args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        };

        processor.ProcessErrorAsync += _ => Task.CompletedTask;

        await processor.StartProcessingAsync(cancellationToken);

        try
        {
            // Run until cancellation is requested.
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            await processor.StopProcessingAsync(CancellationToken.None);
        }
    }
}
