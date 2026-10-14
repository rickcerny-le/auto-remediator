using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AutoRemediator.Infrastructure.AzureDevOps;

/// <summary>The outcome of one <c>git</c> invocation. Stdout is kept as bytes so archives survive intact.</summary>
internal sealed record GitResult(int ExitCode, byte[] Bytes, string Error)
{
    public bool Succeeded => ExitCode == 0;

    public string Text => Encoding.UTF8.GetString(Bytes);
}

/// <summary>
/// Runs the <c>git</c> binary. Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, never
/// string concatenation, and prompting is disabled so a misconfigured repository fails instead of hanging.
/// Shared by the local repository client and the local pull request diff reader.
/// </summary>
internal sealed class GitCli
{
    public const string MissingGitMessage = "git was not found on PATH; it is required when AzureDevOps:Mode is Local.";

    /// <summary>Runs git in <paramref name="workingDirectory"/>, which must already exist.</summary>
    public async Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("Could not start git.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(MissingGitMessage, ex);
        }

        using (process)
        {
            process.StandardInput.Close();

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already exited.
                }
            });

            var output = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output, CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);

            await process.WaitForExitAsync(CancellationToken.None);
            await copy;
            var errorText = await error;
            cancellationToken.ThrowIfCancellationRequested();

            return new GitResult(process.ExitCode, output.ToArray(), errorText);
        }
    }

    /// <summary>Like <see cref="RunAsync"/> but throws <see cref="InvalidOperationException"/> on a non-zero exit.</summary>
    public async Task<GitResult> RequireAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(workingDirectory, args, cancellationToken);
        return result.Succeeded
            ? result
            : throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {result.Error.Trim()}");
    }
}
