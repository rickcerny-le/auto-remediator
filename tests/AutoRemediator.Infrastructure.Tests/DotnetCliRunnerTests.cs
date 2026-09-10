using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>Exercises the real `dotnet` process plumbing — the SDK is present wherever these run.</summary>
public class DotnetCliRunnerTests
{
    private static DotnetCliRunner Runner() => new(NullLogger<DotnetCliRunner>.Instance);

    private static readonly TimeSpan Generous = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Captures_output_of_a_successful_command()
    {
        var result = await Runner().RunAsync("--version", Path.GetTempPath(), Generous, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"\d+\.\d+", result.Output);
    }

    [Fact]
    public async Task Reports_a_nonzero_exit_code_and_still_captures_output()
    {
        var result = await Runner().RunAsync(
            "run --project does-not-exist.csproj", Path.GetTempPath(), Generous, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.False(result.TimedOut);
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(result.Output);
    }

    [Fact]
    public async Task Times_out_rather_than_hanging()
    {
        // Restoring a non-existent project in a temp directory is slow enough that a 1ms
        // budget always elapses first; the point is that we return instead of blocking.
        var result = await Runner().RunAsync(
            "restore", Path.GetTempPath(), TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken);

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_is_not_reported_as_a_timeout()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Runner().RunAsync("restore", Path.GetTempPath(), Generous, cancellation.Token));
    }
}
