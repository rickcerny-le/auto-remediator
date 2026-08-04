using AutoRemediator.Contracts;

var builder = DistributedApplication.CreateBuilder(args);

// --- Backing resources (emulated locally, real Azure when deployed) ---
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();
var tables = storage.AddTables("tables");
var blobs = storage.AddBlobs("blobs");

var serviceBus = builder.AddAzureServiceBus("servicebus")
    .RunAsEmulator();
serviceBus.AddServiceBusQueue(RemediationQueues.RemediationRuns);

// --- API (minimal API, vertical slice) ---
var api = builder.AddProject<Projects.AutoRemediator_Api>("api")
    .WithReference(tables)
    .WithReference(blobs)
    .WithReference(serviceBus)
    .WaitFor(storage)
    .WaitFor(serviceBus);

// --- Web UI (Blazor Web App, Auto) ---
builder.AddProject<Projects.AutoRemediator_Web>("web")
    .WithReference(api)
    .WaitFor(api);

// --- Scheduler worker (deployed as a scheduled/cron ACA Job) ---
builder.AddProject<Projects.AutoRemediator_Worker_Scheduler>("scheduler")
    .WithReference(serviceBus)
    .WaitFor(serviceBus);

// --- Remediation worker (deployed as an event-driven, queue-scaled ACA Job) ---
builder.AddProject<Projects.AutoRemediator_Worker_Remediation>("remediation")
    .WithReference(tables)
    .WithReference(blobs)
    .WithReference(serviceBus)
    .WaitFor(serviceBus);

builder.Build().Run();
