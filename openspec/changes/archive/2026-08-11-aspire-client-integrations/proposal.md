## Why

`AutoRemediator.Infrastructure` already referenced all three Aspire client integration packages (`Aspire.Azure.Data.Tables`, `Aspire.Azure.Storage.Blobs`, `Aspire.Azure.Messaging.ServiceBus`) but never called them. The Storage and Service Bus clients were constructed by hand, reimplementing the one thing those integrations exist to do — deciding per configured value whether it is a connection string or a service endpoint needing a credential — while forgoing what they add: a health check, tracing and metrics per resource.

Two defects followed from the hand-rolled path and are fixed here. The client factories failed **lazily**, so a missing AppHost reference stayed invisible until first use; the scheduler had never been given the Storage references it needs to read enrolled repositories, and nothing surfaced it. And passing a credential explicitly forces the credential code path even when the configured value is a connection string, which breaks the local emulators: the Service Bus health check builds its own client from an empty `FullyQualifiedNamespace` and throws, leaving `/health` permanently unhealthy locally.

This change is already implemented; the specs are being brought in line with it.

## What Changes

- **MODIFIED**: the Storage and Service Bus clients are registered through the Aspire client integrations (`AddAzureTableServiceClient`, `AddAzureBlobServiceClient`, `AddAzureServiceBusClient`) instead of hand-written factories. Dual-mode selection (connection string vs endpoint + credential) is unchanged in behavior but is now delegated rather than reimplemented.
- **MODIFIED**: each Azure resource now contributes a health check, so `/health` genuinely probes Storage and Service Bus rather than reporting healthy on process liveness alone. Traces and metrics for those calls now reach the Aspire dashboard.
- **MODIFIED**: `AddInfrastructure` no longer builds a `DefaultAzureCredential` from the `AZURE_CLIENT_ID` **configuration value**. Credentials are left to the integrations' own default, which selects the user-assigned identity from the `AZURE_CLIENT_ID` **environment variable** — how the deployment actually supplies it. **BREAKING** for any environment that supplied `AZURE_CLIENT_ID` only through a non-environment configuration source; the Terraform deployment injects it as a container-app environment variable, so no deployed environment is affected.
- **MODIFIED**: the Service Bus integration is given a health-check queue name, without which its health check cannot verify anything beyond client construction.
- **Fix**: the AppHost gives the scheduler and remediation workers the `tables` and `blobs` references and a `WaitFor(storage)`. The scheduler reads enrolled repositories from Table Storage and previously crashed at startup once client construction became eager.
- **Fix**: the local storage emulator is started with `--skipApiVersionCheck`. Azurite rejects the storage SDK's current `x-ms-version` on container creation with a bare 400, so blob operations failed locally.
- Key Vault configuration is untouched: `KeyVaultConfigurationExtensions` still pins the managed identity from the `AZURE_CLIENT_ID` configuration value for its own credential.

## Capabilities

### New Capabilities
*(none — this changes how existing capabilities are implemented and observed)*

### Modified Capabilities
- `managed-identity-auth`: dual-mode client construction is delegated to the Aspire client integrations rather than selected by our own inspection of the configured value; user-assigned identity selection for the Azure data clients now comes from the `AZURE_CLIENT_ID` environment variable read by `DefaultAzureCredential`, not from a configuration value we read and pass; each Azure client registration contributes a health check.
- `service-orchestration`: the AppHost must give every service that calls `AddInfrastructure` all three connection references and wait for the backing resources, because client construction is validated at registration; the storage emulator needs `--skipApiVersionCheck` for blob operations to work locally.

## Impact

**Code**
- `src/AutoRemediator.Infrastructure/InfrastructureExtensions.cs` — Aspire integrations replace the hand-written factories; `CreateCredential`, `IsHttpEndpoint`, `IsConnectionString` and `RequireConnectionString` deleted (net −66 lines).
- `src/AutoRemediator.AppHost/AppHost.cs` — scheduler and remediation gain `tables`/`blobs` references and `WaitFor(storage)`; the storage emulator gains `--skipApiVersionCheck`.
- `tests/AutoRemediator.Infrastructure.Tests/InfrastructureExtensionsTests.cs` — asserts the Azure clients resolve and that each integration registered a health check.

**Behavior**
- Missing connection configuration now fails at startup rather than on first use. This is what exposed the scheduler wiring gap and is the desired behavior, but it means any service calling `AddInfrastructure` must be given all three references.
- `/health` can now return 503 for a genuine dependency failure, where previously it only reflected process liveness.

**Not affected**
- No change to the storage/Service Bus abstractions (`ITableStore`, `IBlobStore`, `IMessagePublisher`, `IMessageConsumer`) or to any consumer of them.
- The Azure DevOps `HttpClient` registration, which already inherits Aspire resilience from `ServiceDefaults`.
- Key Vault configuration sourcing.
