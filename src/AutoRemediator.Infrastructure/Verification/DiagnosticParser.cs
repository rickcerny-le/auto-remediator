using System.Text.RegularExpressions;
using AutoRemediator.Domain;

namespace AutoRemediator.Infrastructure.Verification;

/// <summary>
/// Parses MSBuild/NuGet console output into structured diagnostics, and decides whether a failure
/// is attributable to the dependency change or to the environment it ran in.
/// </summary>
internal static partial class DiagnosticParser
{
    /// <summary>`path(line,col): error CODE: message` — the MSBuild canonical diagnostic form.</summary>
    [GeneratedRegex(
        @"^(?<path>[^(\r\n]+?)\((?<line>\d+)(?:,(?<column>\d+))?\)\s*:\s*(?:error|ERROR)\s+(?<code>[A-Za-z]+[0-9]+)\s*:\s*(?<message>.+?)(?:\s*\[[^\]]*\])?$",
        RegexOptions.ExplicitCapture)]
    private static partial Regex FileDiagnostic { get; }

    /// <summary>A pathless `error CODE: message`, as NuGet restore emits.</summary>
    [GeneratedRegex(
        @"^\s*(?:error|ERROR)\s+(?<code>[A-Za-z]+[0-9]+)\s*:\s*(?<message>.+?)(?:\s*\[[^\]]*\])?$",
        RegexOptions.ExplicitCapture)]
    private static partial Regex PlainDiagnostic { get; }

    /// <summary>
    /// NuGet codes that indicate the environment rather than the bump: an unreachable or
    /// unauthenticated feed, or a source that could not be loaded.
    /// </summary>
    private static readonly HashSet<string> EnvironmentCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "NU1301", // Unable to load the service index for source.
        "NU1302", // Source does not support package retrieval.
        "NU1303", // Invalid source credentials.
        "NU1401", // Feature not supported by the source.
        "NU3028", // Signature validation could not reach the timestamp authority.
        "NU3037", // Signature validation failed for environment reasons.
    };

    /// <summary>Phrases that mark an SDK/toolchain problem rather than a dependency problem.</summary>
    private static readonly string[] EnvironmentPhrases =
    [
        "sdk 'microsoft.net.sdk' specified could not be found",
        "requested sdk version",
        "global.json",
        "a compatible .net sdk was not found",
        "no .net sdks were found",
        "the framework 'microsoft.netcore.app'",
        "unable to load the service index",
        "connection refused",
        "no such host is known",
        "the ssl connection could not be established",
        "response status code does not indicate success: 401",
        "response status code does not indicate success: 403",
        "response status code does not indicate success: 5",
    ];

    /// <summary>Reads every `error` diagnostic out of a restore or build log, in order.</summary>
    public static IReadOnlyList<VerificationDiagnostic> Parse(string output, string repositoryRoot)
    {
        var diagnostics = new List<VerificationDiagnostic>();

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r').TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            var withFile = FileDiagnostic.Match(line);
            if (withFile.Success)
            {
                diagnostics.Add(new VerificationDiagnostic(
                    withFile.Groups["code"].Value,
                    withFile.Groups["message"].Value.Trim(),
                    Relativize(withFile.Groups["path"].Value.Trim(), repositoryRoot),
                    int.Parse(withFile.Groups["line"].Value),
                    withFile.Groups["column"].Success ? int.Parse(withFile.Groups["column"].Value) : null));
                continue;
            }

            var plain = PlainDiagnostic.Match(line);
            if (plain.Success)
            {
                diagnostics.Add(new VerificationDiagnostic(
                    plain.Groups["code"].Value,
                    plain.Groups["message"].Value.Trim()));
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// True when the diagnostics positively attribute the failure to the dependency change.
    /// Anything unrecognized is deliberately not attributed — the caller degrades to Skipped, since
    /// wrongly blaming the bump would stop producing pull requests for an environment problem.
    /// </summary>
    public static bool IsDependencyFailure(IReadOnlyList<VerificationDiagnostic> diagnostics, string output)
    {
        if (LooksEnvironmental(output))
        {
            return false;
        }

        return diagnostics.Any(d => IsDependencyCode(d.Code));
    }

    /// <summary>A human-readable reason for a Skipped classification, drawn from the output.</summary>
    public static string DescribeEnvironmentFailure(IReadOnlyList<VerificationDiagnostic> diagnostics, string output)
    {
        var environmental = diagnostics.FirstOrDefault(d => EnvironmentCodes.Contains(d.Code));
        if (environmental is not null)
        {
            return $"{environmental.Code}: {environmental.Message}";
        }

        var phrase = EnvironmentPhrases.FirstOrDefault(p => output.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (phrase is not null)
        {
            return $"the build environment could not run verification (matched '{phrase}')";
        }

        return diagnostics.Count == 0
            ? "verification failed without any recognizable diagnostic"
            : $"verification failed with unattributable diagnostics ({diagnostics[0].Code}: {diagnostics[0].Message})";
    }

    /// <summary>NuGet (NU) and compiler (CS) errors, excluding the environmental NU codes.</summary>
    private static bool IsDependencyCode(string code)
    {
        if (EnvironmentCodes.Contains(code))
        {
            return false;
        }

        return code.StartsWith("NU", StringComparison.OrdinalIgnoreCase)
               || code.StartsWith("CS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksEnvironmental(string output)
        => EnvironmentPhrases.Any(p => output.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Rewrites an absolute path under the workspace as repository-relative with forward slashes, so
    /// it survives the temporary directory and means something in the UI.
    /// </summary>
    private static string Relativize(string path, string repositoryRoot)
    {
        if (repositoryRoot.Length == 0)
        {
            return Normalize(path);
        }

        try
        {
            if (Path.IsPathRooted(path))
            {
                var full = Path.GetFullPath(path);
                var root = Path.GetFullPath(repositoryRoot);
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return Normalize(Path.GetRelativePath(root, full));
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Not a usable path — keep whatever the compiler printed.
        }

        return Normalize(path);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
