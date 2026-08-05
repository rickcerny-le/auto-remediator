using AutoRemediator.Infrastructure;
using AutoRemediator.Worker.Scheduler;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyVaultConfiguration();
builder.AddInfrastructure(); // also registers TimeProvider.System
builder.Services.AddHostedService<SchedulerWorker>();

var host = builder.Build();
host.Run();
