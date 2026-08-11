using AutoRemediator.Contracts;

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
// Reads the enrolled repositories from Table Storage before enqueueing, so it needs storage
// as well as the queue. Every service shares one AddInfrastructure, so all three connection
// values must be present for any of them to start.
builder.AddProject<Projects.AutoRemediator_Worker_Scheduler>("scheduler")
    .WithReference(tables)
    .WithReference(blobs)
    .WithReference(serviceBus)
    .WaitFor(storage)
    .WaitFor(serviceBus);

// --- Remediation worker (deployed as an event-driven, queue-scaled ACA Job) ---
builder.AddProject<Projects.AutoRemediator_Worker_Remediation>("remediation")
    .WithReference(tables)
    .WithReference(blobs)
    .WithReference(serviceBus)
    .WaitFor(storage)
    .WaitFor(serviceBus);

builder.Build().Run();
