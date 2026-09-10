using System.Text.Json;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Review;
using AutoRemediator.Worker.Remediation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoRemediator.Workers.Tests;

public class ReviewCommandWorkerTests
{
    [Theory]
    [InlineData(ReviewCommand.Approve)]
    [InlineData(ReviewCommand.Rebuild)]
    [InlineData(ReviewCommand.Retry)]
    [InlineData(ReviewCommand.Discard)]
    public async Task A_well_formed_message_invokes_the_handler_exactly_once_with_that_command(ReviewCommand command)
    {
        var runId = Guid.NewGuid();
        var request = new ReviewCommandRequested(runId, command, DateTimeOffset.UtcNow);
        var consumer = new SingleMessageConsumer(JsonSerializer.Serialize(request));
        var handler = new RecordingHandler();

        var worker = await RunAsync(consumer, handler);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        var received = Assert.Single(handler.Received);
        Assert.Equal(runId, received.RunId);
        Assert.Equal(command, received.Command);
    }

    [Fact]
    public async Task An_unparseable_body_is_completed_without_invoking_the_handler()
    {
        var consumer = new SingleMessageConsumer("{ not valid json at all");
        var handler = new RecordingHandler();

        var worker = await RunAsync(consumer, handler);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Empty(handler.Received);
    }

    [Fact]
    public async Task A_message_for_an_unknown_run_id_reaches_no_azure_devops_method()
    {
        var request = new ReviewCommandRequested(Guid.NewGuid(), ReviewCommand.Approve, DateTimeOffset.UtcNow);
        var consumer = new SingleMessageConsumer(JsonSerializer.Serialize(request));
        var ado = new NeverCalledAdo();

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();
        builder.Services.RemoveAll<IRemediationRunStore>();
        builder.Services.RemoveAll<IAzureDevOpsClient>();
        builder.Services.AddSingleton<IRemediationRunStore>(new EmptyRunStore());
        builder.Services.AddSingleton<IAzureDevOpsClient>(ado);

        await using var provider = builder.Services.BuildServiceProvider();

        var worker = new ReviewCommandWorker(
            consumer,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ReviewCommandWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await consumer.Completed;
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(ado.WasCalled);
    }

    private sealed class EmptyRunStore : IRemediationRunStore
    {
        public Task SaveAsync(RemediationRun run, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<RemediationRun>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemediationRun>>([]);
        public Task<IReadOnlyList<RemediationRun>> ListAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemediationRun>>([]);
        public Task<RemediationRun?> GetAsync(Guid runId, CancellationToken ct = default) => Task.FromResult<RemediationRun?>(null);
        public Task<RemediationRun?> FindAwaitingReviewAsync(Guid repositoryId, CancellationToken ct = default) => Task.FromResult<RemediationRun?>(null);
    }

    private static async Task<ReviewCommandWorker> RunAsync(SingleMessageConsumer consumer, RecordingHandler handler)
    {
        var services = new ServiceCollection();
        services.AddScoped<IReviewCommandHandler>(_ => handler);
        using var provider = services.BuildServiceProvider();

        var worker = new ReviewCommandWorker(
            consumer,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ReviewCommandWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await consumer.Completed;
        return worker;
    }

    private sealed class RecordingHandler : IReviewCommandHandler
    {
        public List<(Guid RunId, ReviewCommand Command)> Received { get; } = [];

        public Task HandleAsync(Guid runId, ReviewCommand command, CancellationToken cancellationToken = default)
        {
            Received.Add((runId, command));
            return Task.CompletedTask;
        }
    }

    private sealed class NeverCalledAdo : IAzureDevOpsClient
    {
        public bool WasCalled { get; private set; }

        public Task<bool> RepositoryExistsAsync(Domain.ManagedRepository r, CancellationToken ct = default) { WasCalled = true; return Task.FromResult(false); }
        public Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(Domain.ManagedRepository r, CancellationToken ct = default) { WasCalled = true; return Task.FromResult<IReadOnlyList<RepositoryFile>>([]); }
        public Task<string?> GetBranchHeadAsync(Domain.ManagedRepository r, string branch, CancellationToken ct = default) { WasCalled = true; return Task.FromResult<string?>(null); }
        public Task<Stream> GetRepositoryArchiveAsync(Domain.ManagedRepository r, string commitId, CancellationToken ct = default) { WasCalled = true; return Task.FromResult<Stream>(new MemoryStream()); }
        public Task PushFilesAsync(Domain.ManagedRepository r, string branch, string baseCommitId, IReadOnlyList<FileChange> changes, string message, CancellationToken ct = default) { WasCalled = true; return Task.CompletedTask; }
        public Task<string> EnsurePullRequestAsync(Domain.ManagedRepository r, string s, string t, string title, string desc, CancellationToken ct = default) { WasCalled = true; return Task.FromResult("https://pr"); }
    }
}
