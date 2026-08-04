using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Adds Azure Key Vault as a configuration source when a vault URI is configured.
/// Secret names use "--" in place of ":" (e.g. <c>AzureDevOps--Pat</c> surfaces as
/// <c>AzureDevOps:Pat</c>). Locally, with no vault URI, this is a no-op and secrets
/// come from user-secrets/configuration instead.
/// </summary>
public static class KeyVaultConfigurationExtensions
{
    public const string KeyVaultUriKey = "KeyVaultUri";

    public static TBuilder AddKeyVaultConfiguration<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var vaultUri = builder.Configuration[KeyVaultUriKey];
        if (string.IsNullOrWhiteSpace(vaultUri))
        {
            return builder;
        }

        var options = new DefaultAzureCredentialOptions();
        var clientId = builder.Configuration["AZURE_CLIENT_ID"];
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            options.ManagedIdentityClientId = clientId;
        }

        builder.Configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential(options));
        return builder;
    }
}
