using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AutoRemediator.Api.Tests;

/// <summary>
/// Boots the API in-memory. Supplies fake connection strings (normally injected by the
/// Aspire AppHost) so infrastructure registration does not fail during the test host build.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:tables", "UseDevelopmentStorage=true");
        builder.UseSetting("ConnectionStrings:blobs", "UseDevelopmentStorage=true");
        builder.UseSetting("ConnectionStrings:servicebus",
            "Endpoint=sb://localhost/;SharedAccessKeyName=key;SharedAccessKey=a2V5a2V5a2V5a2V5a2V5a2V5");
    }
}
