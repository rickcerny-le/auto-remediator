namespace LocalScenarioSetup;

internal sealed record MissingTool(string Tool, string Hint);

internal static class Preflight
{
    private static readonly IReadOnlyDictionary<string, string> Hints = new Dictionary<string, string>
    {
        ["pwsh"] = "PowerShell 7 (pwsh) is required for local scenarios; Windows PowerShell 5.1 is not supported. Install it from https://aka.ms/powershell, then aspire stop / aspire start.",
        ["git"] = "git is required for local repository mode.",
        ["dotnet"] = ".NET 10 SDK is required to build the scenario feed and repos.",
    };

    public static IReadOnlyList<string> RequiredTools(string mode) => mode switch
    {
        "provision" => ["pwsh", "git", "dotnet"],
        "reset" => ["pwsh", "git"],
        "enroll" => ["pwsh"],
        _ => throw new ArgumentException($"Unknown mode '{mode}'.", nameof(mode)),
    };

    /// <summary>Returns the required tools that the locator cannot resolve.</summary>
    public static IReadOnlyList<MissingTool> Check(string mode, Func<string, string?> locator) =>
        RequiredTools(mode)
            .Where(tool => locator(tool) is null)
            .Select(tool => new MissingTool(tool, Hints[tool]))
            .ToList();
}
