using AutoRemediator.Agents;
using AutoRemediator.Infrastructure;
using AutoRemediator.Worker.Remediation;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyVaultConfiguration();
builder.AddInfrastructure();
builder.AddAgents();
builder.Services.AddHostedService<RemediationWorker>();
builder.Services.AddHostedService<ReviewCommandWorker>();

var host = builder.Build();
host.Run();
