using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Messaging;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Workers.Tests;

internal sealed class FakeManagedRepositoryStore(IReadOnlyList<ManagedRepository> repositories) : IManagedRepositoryStore
{
    public Task<IReadOnlyList<ManagedRepository>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(repositories);

    public Task<ManagedRepository?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(repositories.FirstOrDefault(r => r.Id == id));

    public Task UpsertAsync(ManagedRepository repository, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed record PublishedMessage(string Queue, object Message);

internal sealed class RecordingPublisher : IMessagePublisher
{
    public List<PublishedMessage> Messages { get; } = [];

    public Task PublishAsync<T>(string queue, T message, CancellationToken cancellationToken = default)
    {
        Messages.Add(new PublishedMessage(queue, message!));
        return Task.CompletedTask;
    }
}

/// <summary>Delivers exactly one message body to the handler, then signals completion.</summary>
internal sealed class SingleMessageConsumer(string body) : IMessageConsumer
{
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completed => _completed.Task;

    public async Task RunAsync(string queue, Func<string, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        await handler(body, cancellationToken);
        _completed.TrySetResult();
    }
}

internal sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _stopping = new();
    public bool StopRequested { get; private set; }

    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication()
    {
        StopRequested = true;
        _stopping.Cancel();
    }
}
