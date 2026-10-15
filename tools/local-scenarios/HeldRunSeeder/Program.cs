using System.Diagnostics;
using System.Text;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Review;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Seeds a held (AwaitingReview) run for the `held-review` demo repository. The run is real in the way
// that matters: its base commit is the repo's actual `main` head and its files come from the repo's
// `scenario/agent-fix` branch, so Approve, Rebuild and Discard exercise the local client and real
// verification. Runs as an explicit-start Aspire resource (AppHost, local mode only), which injects
// the Azurite connection strings and the AzureDevOps__* local settings. It can also be run by hand
// with ConnectionStrings__tables / ConnectionStrings__blobs set.

const string SourceBranch = "scenario/agent-fix";

var repoName = "held-review";
string? rootArg = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--root" when i + 1 < args.Length:
            rootArg = args[++i];
            break;
        case "--repo" when i + 1 < args.Length:
            repoName = args[++i];
            break;
        default:
            return Fail($"Unknown or incomplete argument '{args[i]}'. Usage: HeldRunSeeder [--root <path>] [--repo <name>]");
    }
}

var builder = Host.CreateApplicationBuilder();

var missing = new[] { "ConnectionStrings:tables", "ConnectionStrings:blobs" }
    .Where(key => string.IsNullOrWhiteSpace(builder.Configuration[key]))
    .Select(key => key.Replace(':', '_').Replace("_", "__"))
    .ToList();
if (missing.Count > 0)
{
    return Fail(
        $"Missing connection string(s): {string.Join(", ", missing)}. Start this tool from the Aspire dashboard " +
        "(the explicit-start 'held-run-seeder' resource, present when LocalRepos__Enabled=true), or set " +
        "ConnectionStrings__tables and ConnectionStrings__blobs for Azurite.");
}

// The seeder never talks to Service Bus, but AddInfrastructure registers a client for it.
if (string.IsNullOrWhiteSpace(builder.Configuration["ConnectionStrings:servicebus"]))
{
    builder.Configuration["ConnectionStrings:servicebus"] =
        "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
}

var root = rootArg ?? builder.Configuration["AzureDevOps:LocalRoot"];
if (string.IsNullOrWhiteSpace(root))
{
    root = FindRepoRoot() is { } repoRoot ? Path.Combine(repoRoot, ".local", "repos") : null;
}

if (string.IsNullOrWhiteSpace(root))
{
    return Fail("Could not find the local repositories root. Pass --root <path> (the folder that contains the demo repos).");
}

root = Path.GetFullPath(root);
builder.Configuration["AzureDevOps:Mode"] = "Local";
builder.Configuration["AzureDevOps:LocalRoot"] = root;
builder.AddInfrastructure();

using var host = builder.Build();
var services = host.Services;

var repositories = await services.GetRequiredService<IManagedRepositoryStore>().ListAsync();
var repo = repositories.FirstOrDefault(r => string.Equals(r.Name, repoName, StringComparison.Ordinal));
if (repo is null)
{
    return Fail($"Repository '{repoName}' is not enrolled. Run tools/local-scenarios/Set-LocalScenarioConfig.ps1 first.");
}

var runStore = services.GetRequiredService<IRemediationRunStore>();
var existing = await runStore.FindAwaitingReviewAsync(repo.Id);
if (existing is not null)
{
    Console.WriteLine($"A held run already exists for {repo.Slug}: {existing.Id}");
    Console.WriteLine($"Web: /runs/{existing.Id}");
    return 0;
}

var repoPath = Path.Combine(root, repoName);
if (!Directory.Exists(repoPath))
{
    return Fail($"No git repository folder at '{repoPath}'. Run tools/local-scenarios/New-LocalScenarios.ps1 first.");
}

var baseCommit = await services.GetRequiredService<IAzureDevOpsClient>().GetBranchHeadAsync(repo, repo.TargetBranch);
if (string.IsNullOrWhiteSpace(baseCommit))
{
    return Fail($"Branch '{repo.TargetBranch}' was not found in '{repoPath}'.");
}

string[] changed;
try
{
    changed = (await Git(repoPath, "diff", "--name-only", "--diff-filter=AM", repo.TargetBranch, SourceBranch))
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
catch (InvalidOperationException ex)
{
    return Fail($"Could not diff '{repo.TargetBranch}' against '{SourceBranch}': {ex.Message}");
}

if (changed.Length == 0)
{
    return Fail($"Branch '{SourceBranch}' has no changes against '{repo.TargetBranch}' in '{repoPath}'.");
}

var files = new List<ProposedFile>();
foreach (var path in changed)
{
    var before = await TryGit(repoPath, "show", $"{repo.TargetBranch}:{path}");
    var after = await Git(repoPath, "show", $"{SourceBranch}:{path}");
    var origin = IsManifest(path) ? ProposedFileOrigin.Manifest : ProposedFileOrigin.AgentEdit;
    files.Add(new ProposedFile("/" + path, before, after, origin));
}

var firstEdit = files.FirstOrDefault(f => f.Origin == ProposedFileOrigin.AgentEdit);
if (firstEdit is null)
{
    return Fail($"Branch '{SourceBranch}' changes only manifests; a held proposal needs at least one source edit.");
}

var now = DateTimeOffset.UtcNow;
var run = new RemediationRun(Guid.NewGuid(), repo.Id, repo.Slug, now.AddMinutes(-5));

// Same order as RemediationRunner's held branch, so every state transition is legal.
run.RecordUpdates([new DependencyUpdate("Northwind.Messaging", "3.2.0", "4.0.0")]);
run.Advance(RunStatus.Remediating);

var logs = services.GetRequiredService<IVerificationLogStore>();
var transcriptReference = await logs.StoreAsync(run.Id, Transcript(repo.Slug, firstEdit.Path), "remediation-transcript.md");
run.Remediated(1, transcriptReference);

var proposal = new ChangeProposal(
    run.Id, repo.Id, baseCommit, now,
    files,
    [new VerificationDiagnostic(
        "CS1061",
        "'MessageBus' does not contain a definition for 'Publish' and no accessible extension method 'Publish' accepting a first argument of type 'MessageBus' could be found",
        firstEdit.Path.TrimStart('/'), 1, 38)],
    attempts: 1,
    transcriptReference: transcriptReference);

var reference = await services.GetRequiredService<IChangeProposalStore>().StoreAsync(proposal);
run.AwaitingReview(reference, now);
await runStore.SaveAsync(run);

Console.WriteLine($"Seeded held run {run.Id} for {repo.Slug} (base {baseCommit[..Math.Min(12, baseCommit.Length)]}, {files.Count} file(s)).");
Console.WriteLine($"Web: /runs/{run.Id}");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static bool IsManifest(string path)
{
    var name = Path.GetFileName(path);
    return name.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".props", StringComparison.OrdinalIgnoreCase);
}

static string? FindRepoRoot()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AutoRemediator.sln")))
            {
                return dir.FullName;
            }
        }
    }

    return null;
}

static string Transcript(string slug, string editedPath) => $"""
    # Remediation transcript (seeded)

    This run was seeded for local testing by `HeldRunSeeder`. No model ran and nothing was verified by an agent.

    - Repository: {slug}
    - Update: Northwind.Messaging 3.2.0 -> 4.0.0
    - Provoking diagnostic: CS1061 `MessageBus` does not contain a definition for `Publish`
    - Attempt 1: replaced the removed `Publish(string, string)` call with `PublishAsync(Message)` in `{editedPath}`.

    The proposal's files are the real difference between `main` and `scenario/agent-fix`.
    """;

static async Task<string?> TryGit(string repoPath, params string[] gitArgs)
{
    try
    {
        return await Git(repoPath, gitArgs);
    }
    catch (InvalidOperationException)
    {
        return null;
    }
}

static async Task<string> Git(string repoPath, params string[] gitArgs)
{
    var psi = new ProcessStartInfo("git")
    {
        WorkingDirectory = repoPath,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = new UTF8Encoding(false),
        UseShellExecute = false,
    };
    psi.ArgumentList.Add("-c");
    psi.ArgumentList.Add("core.quotepath=false");
    foreach (var arg in gitArgs)
    {
        psi.ArgumentList.Add(arg);
    }

    using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start git.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"git {string.Join(' ', gitArgs)} failed: {(await stderr).Trim()}");
    }

    return await stdout;
}
