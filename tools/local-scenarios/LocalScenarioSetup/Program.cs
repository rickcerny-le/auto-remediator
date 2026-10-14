using System.Diagnostics;
using LocalScenarioSetup;

var options = Options.Parse(args, out var parseError);
if (options is null)
{
    Console.Error.WriteLine(parseError);
    Console.Error.WriteLine(Options.Usage);
    return 1;
}

// enroll: validate the profile before anything else, so a bad LocalRepos__Profile fails this resource only.
if (options.Mode == "enroll")
{
    var profileError = Enrollment.ValidateProfile(options.Profile);
    if (profileError is not null)
    {
        Console.Error.WriteLine(profileError);
        Console.Error.Flush();
        return 1;
    }
}

var path = Environment.GetEnvironmentVariable("PATH");
var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
string? Locate(string tool) => ToolLocator.Find(tool, path, pathExt);

var missing = Preflight.Check(options.Mode, Locate);
if (missing.Count > 0)
{
    foreach (var m in missing)
    {
        Console.Error.WriteLine($"LocalRepos is enabled but {m.Tool} was not found on PATH. {m.Hint}");
    }

    Console.Error.Flush();
    return 2;
}

Console.Out.WriteLine($"local-scenarios: {options.Mode} root={options.Root}");
Console.Out.Flush();

IReadOnlyList<string> scriptArgs;
var startInfoEnv = new Dictionary<string, string>();
if (options.Mode == "enroll")
{
    // The URL comes from Aspire service discovery (WithReference(api)); there is no fixed port.
    var apiUrl = Enrollment.ResolveApiUrl(Environment.GetEnvironmentVariable);
    if (apiUrl is null)
    {
        Console.Error.WriteLine(Enrollment.NoApiUrlMessage);
        Console.Error.Flush();
        return 1;
    }

    Console.Out.WriteLine($"local-scenarios: enrolling profile {options.Profile} through {apiUrl}");
    Console.Out.Flush();

    // A guard on top of WaitFor(api): the health check can lag the Running state.
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var healthError = await Enrollment.WaitForHealthyAsync(
        http, apiUrl, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(120),
        message => { Console.Out.WriteLine($"local-scenarios: {message}"); Console.Out.Flush(); });
    if (healthError is not null)
    {
        Console.Error.WriteLine($"The API did not become healthy within 120 s. Last error: {healthError}");
        Console.Error.Flush();
        return 1;
    }

    scriptArgs = ScriptCommand.ForEnroll(options.Scripts, options.Root, options.Profile!, apiUrl);
    startInfoEnv["LOCAL_SCENARIOS_STARTUP"] = "1";
}
else
{
    scriptArgs = ScriptCommand.For(options.Mode, options.Scripts, options.Root, options.NoReset);
}

var psi = new ProcessStartInfo(Locate("pwsh")!)
{
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
};
foreach (var arg in scriptArgs)
{
    psi.ArgumentList.Add(arg);
}

foreach (var (key, value) in startInfoEnv)
{
    psi.Environment[key] = value;
}

var stopwatch = Stopwatch.StartNew();
using var process = new Process { StartInfo = psi };
process.OutputDataReceived += (_, e) =>
{
    if (e.Data is not null)
    {
        Console.Out.WriteLine(e.Data);
        Console.Out.Flush();
    }
};
process.ErrorDataReceived += (_, e) =>
{
    if (e.Data is not null)
    {
        Console.Error.WriteLine(e.Data);
        Console.Error.Flush();
    }
};

process.Start();
process.BeginOutputReadLine();
process.BeginErrorReadLine();
process.WaitForExit();

var exitCode = process.ExitCode;
Console.Out.WriteLine(
    $"local-scenarios: {options.Mode} finished with exit code {exitCode} in {stopwatch.Elapsed.TotalSeconds:F1} s");
Console.Out.Flush();
return exitCode;
