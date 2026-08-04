using AutoRemediator.Api.Features;
using AutoRemediator.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyVaultConfiguration();
builder.AddInfrastructure();
builder.Services.AddFeatureHandlers();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapFeatureEndpoints();

app.Run();

// Exposed so the integration test project can boot the API via WebApplicationFactory.
public partial class Program;
