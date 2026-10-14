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

IResourceBuilder<ProjectResource> WithLocalRepos(IResourceBuilder<ProjectResource> project) =>
    localReposEnabled
        ? project
            .WithEnvironment("AzureDevOps__Mode", "Local")
            .WithEnvironment("AzureDevOps__LocalRoot", Path.Combine(localRoot, "repos"))
            .WithEnvironment("AzureDevOps__LocalPullRequestsRoot", Path.Combine(localRoot, "prs"))
        : project;

Console.WriteLine(localReposEnabled
    ? $"AppHost: repository mode Local (root {localRoot}); model {(modelEnabled ? "enabled" : "disabled")}"
    : $"AppHost: repository mode AzureDevOps; model {(modelEnabled ? "enabled" : "disabled")}");

// --- API (minimal API, vertical slice) ---
var api = builder.AddProject<Projects.AutoRemediator_Api>("api")
    .WithReference(tables)
    .WithReference(blobs)
    .WithReference(serviceBus)
    .WaitFor(storage)
    .WaitFor(serviceBus);
WithLocalRepos(api);

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
// AzureDevOps__* settings so it reads the real held-review repo under <root>\repos. Run
// tools/local-scenarios/Set-LocalScenarioConfig.ps1 first so the repo is enrolled.
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
