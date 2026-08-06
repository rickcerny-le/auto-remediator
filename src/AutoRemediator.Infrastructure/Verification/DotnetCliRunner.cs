using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AutoRemediator.Infrastructure.Verification;

/// <summary>The result of one `dotnet` invocation. <see cref="TimedOut"/> implies failure.</summary>
public sealed record CliResult(int ExitCode, string Output, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>Runs the `dotnet` CLI in a working directory and captures its combined output.</summary>
public interface IDotnetCliRunner
{
    Task<CliResult> RunAsync(
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

internal sealed class DotnetCliRunner(ILogger<DotnetCliRunner> logger) : IDotnetCliRunner
{
    public async Task<CliResult> RunAsync(
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Keep restore output deterministic and machine-readable regardless of the host's settings.
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.Environment["NO_COLOR"] = "1";

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();

        // Locked because stdout and stderr drain on separate threads.
        var sync = new object();
        void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (sync)
            {
                output.AppendLine(line);
            }
        }

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);

        logger.LogDebug("dotnet {Arguments} (in {WorkingDirectory})", arguments, workingDirectory);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);

            // A caller-requested cancellation is not a timeout — let it propagate.
            cancellationToken.ThrowIfCancellationRequested();

            lock (sync)
            {
                return new CliResult(-1, output.ToString(), TimedOut: true);
            }
        }

        lock (sync)
        {
            return new CliResult(process.ExitCode, output.ToString(), TimedOut: false);
        }
    }

    private void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            logger.LogDebug(ex, "Could not kill the dotnet process; it may have already exited.");
        }
    }
}
