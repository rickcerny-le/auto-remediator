namespace LocalScenarioSetup;

internal static class ScriptCommand
{
    /// <summary>
    /// Builds the pwsh argument list. Every item is a separate ArgumentList entry, so paths with spaces are
    /// never re-quoted.
    /// </summary>
    public static IReadOnlyList<string> ForEnroll(string scripts, string root, string apiUrl) =>
    [
        "-NoProfile",
        "-NonInteractive",
        "-File",
        Path.Combine(scripts, "Set-LocalScenarioConfig.ps1"),
        "-ApiUrl",
        apiUrl,
        "-Root",
        root,
    ];

    public static IReadOnlyList<string> For(string mode, string scripts, string root, bool noReset)
    {
        var args = new List<string>
        {
            "-NoProfile",
            "-NonInteractive",
            "-File",
            Path.Combine(scripts, "New-LocalScenarios.ps1"),
            "-Root",
            root,
        };

        switch (mode)
        {
            case "provision":
                if (!noReset)
                {
                    args.Add("-ResetIfCurrent");
                }

                break;
            case "reset":
                args.Add("-Reset");
                break;
            default:
                throw new ArgumentException($"Unknown mode '{mode}'.", nameof(mode));
        }

        return args;
    }
}
