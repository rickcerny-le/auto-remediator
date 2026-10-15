namespace LocalScenarioSetup;

internal sealed record Options(string Mode, string Scripts, string Root, bool NoReset)
{
    public const string Usage =
        "Usage: LocalScenarioSetup <provision|reset> --scripts <dir> --root <dir> [--no-reset]\n"
        + "       LocalScenarioSetup enroll --scripts <dir> --root <dir>";

    /// <summary>Parses the command line. Returns null and an error message when it is invalid.</summary>
    public static Options? Parse(string[] args, out string? error)
    {
        error = null;
        if (args.Length == 0)
        {
            error = "Missing mode.";
            return null;
        }

        var mode = args[0];
        if (mode is not ("provision" or "reset" or "enroll"))
        {
            error = $"Unknown mode '{mode}'.";
            return null;
        }

        string? scripts = null;
        string? root = null;
        var noReset = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scripts" when i + 1 < args.Length:
                    scripts = args[++i];
                    break;
                case "--root" when i + 1 < args.Length:
                    root = args[++i];
                    break;
                case "--no-reset":
                    noReset = true;
                    break;
                default:
                    error = $"Unknown or incomplete argument '{args[i]}'.";
                    return null;
            }
        }

        if (string.IsNullOrWhiteSpace(scripts))
        {
            error = "Missing --scripts.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            error = "Missing --root.";
            return null;
        }

        if (noReset && mode != "provision")
        {
            error = "--no-reset applies only to provision.";
            return null;
        }

        return new Options(mode, scripts, root, noReset);
    }
}
