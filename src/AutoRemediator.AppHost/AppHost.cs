using AutoRemediator.Contracts;
using CommunityToolkit.Aspire.Hosting.Ollama;

var builder = DistributedApplication.CreateBuilder(args);

// --- Backing resources (emulated locally, real Azure when deployed) ---
// Azurite rejects the storage SDK's current x-ms-version on container creation with a bare 400,
// so blob operations (run artifacts / verification logs) fail locally without this flag.
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithArgs("--skipApiVersionCheck"));
var tables = storage.AddTables("tables");
var blobs = storage.AddBlobs("blobs");

var serviceBus = builder.AddAzureServiceBus("servicebus")
    .RunAsEmulator();
serviceBus.AddServiceBusQueue(RemediationQueues.RemediationRuns);
serviceBus.AddServiceBusQueue(RemediationQueues.ReviewCommands);

// --- Model for the AI remediation loop (Slice 5 spike) ---
// Runs locally in development as an Ollama container (the first start pulls phi4 into the data
// volume). Set Model:Enabled=false (env var Model__Enabled) to leave it out: no Ollama container,
// no model download, and the loop reports "no model configured". A missing or unparseable value
// means enabled.
var modelEnabled = !bool.TryParse(builder.Configuration["Model:Enabled"], out var enabled) || enabled;
IResourceBuilder<OllamaModelResource>? chat = null;
if (modelEnabled)
{
    chat = builder.AddOllama("ollama")
        .WithDataVolume()
        .AddModel("chat", "phi4");
}

// --- Local repository mode ---
// LocalRepos:Enabled=true (env var LocalRepos__Enabled) points api, scheduler and remediation at
// plain git repositories on disk instead of Azure DevOps: no PAT, organization URL or Key Vault.
// LocalRepos:Root defaults to the repository's .local folder; repos live in <root>\repos and the
// recorded pull requests in <root>\prs. A missing or unparseable value means disabled.
var localReposEnabled = bool.TryParse(builder.Configuration["LocalRepos:Enabled"], out var localEnabled) && localEnabled;
var localRoot = builder.Configuration["LocalRepos:Root"];
if (string.IsNullOrWhiteSpace(localRoot))
{
    localRoot = Path.Combine(builder.AppHostDirectory, "..", "..", ".local");
}
localRoot = Path.GetFullPath(localRoot);

// LocalRepos:ResetOnStart (env var LocalRepos__ResetOnStart) makes the provisioning step also reset
// the repos and prs to a clean state on every start. A missing or unparseable value means true.
var resetOnStart = !bool.TryParse(builder.Configuration["LocalRepos:ResetOnStart"], out var resetValue) || resetValue;
// LocalRepos:RunOnStart (env var LocalRepos__RunOnStart) starts the scheduler automatically once
// provisioning is done. Default false: the scheduler is opt-in in local mode (start it from the
// dashboard or `aspire resource scheduler start`). A missing or unparseable value means false.
var runOnStart = bool.TryParse(builder.Configuration["LocalRepos:RunOnStart"], out var runValue) && runValue;
// LocalRepos:Profile (env var LocalRepos__Profile) is obsolete and ignored: each scenario repo carries its
// own settings (tools/local-scenarios/scenarios.psd1). A non-blank value only prints a notice.
if (!string.IsNullOrWhiteSpace(builder.Configuration["LocalRepos:Profile"]))
{
    Console.WriteLine("AppHost: LocalRepos__Profile is obsolete and ignored: each scenario repo carries its own settings (see docs/local-testing.md).");
}

// Scheduler:DevLoopEnabled / Scheduler:DevLoopIntervalSeconds (env vars Scheduler__DevLoopEnabled and
// Scheduler__DevLoopIntervalSeconds) are forwarded to the scheduler resource when set, so locally it can
// act as a real tick (due and not-due behaviour) with no code edits. Unset: nothing is forwarded.
var devLoopEnabled = builder.Configuration["Scheduler:DevLoopEnabled"];
var devLoopInterval = builder.Configuration["Scheduler:DevLoopIntervalSeconds"];

// --- Local scenario provisioning (local mode only) ---
// local-scenarios builds or confirms <root> (feed, repos, prs) with tools/local-scenarios, and
// resets it unless ResetOnStart=false. It has no WaitFor, so it runs in parallel with the
// emulators' start. api, scheduler, remediation and held-run-seeder wait for it to complete: if it
// exits non-zero they do not start, so nothing reads a half-built <root>.
IResourceBuilder<ProjectResource>? localScenarios = null;
var scriptsDir = string.Empty;
if (localReposEnabled)
{
    scriptsDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "tools", "local-scenarios"));

    var provisionArgs = new List<string> { "provision", "--scripts", scriptsDir, "--root", localRoot };
    if (!resetOnStart)
    {
        provisionArgs.Add("--no-reset");
    }

    localScenarios = builder.AddProject<Projects.LocalScenarioSetup>("local-scenarios")
        .WithArgs(provisionArgs.ToArray());

    // Explicit start: the fast reset between runs, without a rebuild.
    builder.AddProject<Projects.LocalScenarioSetup>("local-scenarios-reset")
        .WithArgs("reset", "--scripts", scriptsDir, "--root", localRoot)
        .WaitForCompletion(localScenarios)
        .WithExplicitStart();
}

IResourceBuilder<ProjectResource> WithLocalRepos(IResourceBuilder<ProjectResource> project) =>
    localReposEnabled
        ? project
            .WithEnvironment("AzureDevOps__Mode", "Local")
            .WithEnvironment("AzureDevOps__LocalRoot", Path.Combine(localRoot, "repos"))
            .WithEnvironment("AzureDevOps__LocalPullRequestsRoot", Path.Combine(localRoot, "prs"))
            .WaitForCompletion(localScenarios!)
        : project;

Console.WriteLine(localReposEnabled
    ? $"AppHost: repository mode Local (root {localRoot}); reset on start {(resetOnStart ? "true" : "false")}; scheduler {(runOnStart ? "runs on start" : "explicit start")}; model {(modelEnabled ? "enabled" : "disabled")}"
    : $"AppHost: repository mode AzureDevOps; model {(modelEnabled ? "enabled" : "disabled")}");
if (!string.IsNullOrWhiteSpace(devLoopEnabled) || !string.IsNullOrWhiteSpace(devLoopInterval))
{
    Console.WriteLine($"AppHost: scheduler dev loop forwarded (Scheduler__DevLoopEnabled={devLoopEnabled ?? "(unset)"}; Scheduler__DevLoopIntervalSeconds={devLoopInterval ?? "(unset)"})");
}

// --- API (minimal API, vertical slice) ---
var api = builder.AddProject<Projects.AutoRemediator_Api>("api")
    .WithReference(tables)
    .WithReference(blobs)
    .WithReference(serviceBus)
    .WaitFor(storage)
    .WaitFor(serviceBus);
WithLocalRepos(api);

// --- Local enrollment (local mode only) ---
// Azurite is not persisted, so the scenario repos are enrolled on every start, after api is healthy.
// The health check is local-mode only so the Azure DevOps composition is unchanged. /health is mapped
// only in Development (ServiceDefaults.MapDefaultEndpoints); Aspire runs projects as Development
// locally, but a non-Development launch profile would make this check fail and block the waiters.
// The enroll resource gets the API URL from service discovery (WithReference), never a fixed port.
// Nothing but the scheduler (and only when RunOnStart=true) waits for it, so a failed enrollment
// (for example an unreachable API) leaves api, web and remediation running.
IResourceBuilder<ProjectResource>? enroll = null;
if (localReposEnabled)
{
    api.WithHttpHealthCheck("/health");

    enroll = builder.AddProject<Projects.LocalScenarioSetup>("local-scenarios-enroll")
        .WithArgs("enroll", "--scripts", scriptsDir, "--root", localRoot)
        .WithReference(api)
        .WaitFor(api);
}

// --- Web UI (React SPA on Vite) ---
// The Vite dev server proxies /api to the API's injected service-discovery URL, so the browser
// sees one origin, as it does through nginx when deployed. npm install runs before it starts.
builder.AddViteApp("web", "../AutoRemediator.Web")
    .WithReference(api)
    .WaitFor(api);

// --- Scheduler worker (deployed as a scheduled/cron ACA Job) ---
// Reads the enrolled repositories from Table Storage before enqueueing, so it needs storage
// as well as the queue. Every service shares one AddInfrastructure, so all three connection
// values must be present for any of them to start.
var scheduler = builder.AddProject<Projects.AutoRemediator_Worker_Scheduler>("scheduler")
    .WithReference(tables)
    .WithReference(blobs)
    .WithReference(serviceBus)
    .WaitFor(storage)
    .WaitFor(serviceBus);
WithLocalRepos(scheduler);
if (!string.IsNullOrWhiteSpace(devLoopEnabled))
{
    scheduler.WithEnvironment("Scheduler__DevLoopEnabled", devLoopEnabled);
}
if (!string.IsNullOrWhiteSpace(devLoopInterval))
{
    scheduler.WithEnvironment("Scheduler__DevLoopIntervalSeconds", devLoopInterval);
}
if (localReposEnabled && !runOnStart)
{
    // Opt-in in local mode: starts on demand (the dashboard, or `aspire resource scheduler start`).
    // It still waits for local-scenarios; enrollment does not gate it by default.
    scheduler.WithExplicitStart();
}
else if (localReposEnabled)
{
    // RunOnStart=true: the startup pass must see the enrolled repos, so wait for enrollment to finish.
    // If enrollment fails the scheduler does not start; fix the cause, start local-scenarios-enroll
    // again, then start the scheduler.
    scheduler.WaitForCompletion(enroll!);
}

// --- Remediation worker (deployed as an event-driven, queue-scaled ACA Job) ---
var remediation = builder.AddProject<Projects.AutoRemediator_Worker_Remediation>("remediation")
    .WithReference(tables)
    .WithReference(blobs)
    .WithReference(serviceBus)
    .WaitFor(storage)
    .WaitFor(serviceBus);
WithLocalRepos(remediation);
if (chat is not null)
{
    remediation.WithReference(chat);
}
// Deliberately no WaitFor(chat): the model is a soft dependency. Waiting on it left the worker
// idle indefinitely while the Ollama container was still pulling the model, stopping dependency
// updates entirely because a repair capability was missing. An unavailable model costs repairs,
// not the pipeline.

// --- Held-for-review seeder (local mode only) ---
// Explicit start: it does nothing until started from the dashboard (or `aspire resource held-run-seeder start`).
// It gets the Azurite connection strings injected like the other resources, plus the local-mode
// AzureDevOps__* settings so it reads the real held-review repo under <root>\repos. It waits for
// local-scenarios (via WithLocalRepos), so the repos exist; it still needs the repo to be
// enrolled, which local-scenarios-enroll does automatically.
if (localReposEnabled)
{
    var heldRunSeeder = builder.AddProject<Projects.HeldRunSeeder>("held-run-seeder")
        .WithReference(tables)
        .WithReference(blobs)
        .WaitFor(storage)
        .WithExplicitStart();
    WithLocalRepos(heldRunSeeder);
}

builder.Build().Run();
