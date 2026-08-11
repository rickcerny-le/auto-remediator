## Context

`AddInfrastructure` built the Azure clients itself:

```csharp
builder.Services.AddSingleton(_ =>
    CreateTableServiceClient(RequireConnectionString(config, "tables"), credential));
// + IsHttpEndpoint() / IsConnectionString() to pick the constructor
// + CreateCredential() to build a DefaultAzureCredential from config["AZURE_CLIENT_ID"]
```

All three Aspire client integration packages were already referenced and unused. The hand-rolled path predates this change and was written before those integrations were wired up.

Two defects traced back to it, both found by running the app through the Aspire CLI rather than `dotnet run`:

1. The factories were lambdas, so construction was **lazy**. A service missing its connection reference did not fail until something resolved the client. The scheduler — which reads enrolled repositories from Table Storage — had only ever been given the Service Bus reference in the AppHost, and nothing surfaced it.
2. `/health` returned 503 while `/alive` returned 200, with `Azure_ServiceBusClient` throwing `The value '' is not a well-formed Service Bus fully qualified namespace`.

## Goals / Non-Goals

**Goals:**
- Register the Azure clients through the Aspire integrations, deleting the reimplemented dual-mode logic.
- Gain per-resource health checks, tracing and metrics.
- Keep the observable dual-mode behavior identical: emulator connection strings locally, endpoint + managed identity in Azure.
- Keep the user-assigned identity selected in Azure.
- Keep the system fully runnable locally against the emulators.

**Non-Goals:**
- Changing the storage/messaging abstractions or any consumer of them.
- Moving Key Vault configuration onto `Aspire.Azure.Security.KeyVault` (that package is not referenced; considered and deferred).
- Changing the Azure DevOps `HttpClient`, which already inherits Aspire resilience via `ServiceDefaults`.
- Adding a detailed `/health` response writer.

## Decisions

### Delegate dual-mode selection to the integrations

The integrations read `ConnectionStrings:<name>` and populate either `ConnectionString` or `ServiceUri`/`FullyQualifiedNamespace` on their settings, which is exactly what `IsHttpEndpoint`/`IsConnectionString` were doing by hand. Delegating removes ~50 lines and, more importantly, removes a second implementation of a rule that has to agree with the SDK's.

### Do not pass a credential explicitly

This was the interesting one, because the obvious defensive choice is wrong.

Passing `settings.Credential` looks harmless, but it makes the integration — and anything downstream reading those settings — treat the resource as credential-authenticated. The Service Bus health check then calls `ServiceBusClientProvider.CreateClient(fullyQualifiedNamespace, credential)`, and under an emulator connection string `FullyQualifiedNamespace` is empty, so it throws:

```
System.ArgumentException: The value '' is not a well-formed Service Bus fully
qualified namespace. (Parameter 'fullyQualifiedNamespace')
```

The result is a health check that can never pass locally.

The original motivation for passing it was that `CreateCredential` read `AZURE_CLIENT_ID` from **configuration**, whereas `DefaultAzureCredential` reads it from the **environment** — so relying on the default looked like a narrowing. Checking the deployment settled it: `infra/terraform/environments/test/main.tf` merges `identity_env` (which is `AZURE_CLIENT_ID`) into every container app's `env_vars`, making it a process environment variable. `DefaultAzureCredential` picks it up unaided.

Chosen: pass no credential. The narrowing is theoretical (an `AZURE_CLIENT_ID` supplied *only* via appsettings or Key Vault would no longer pin the identity), no environment does that, and the alternative — reintroducing connection-string sniffing to decide when it is safe to pass a credential — would restore the very logic this change deletes.

### Set a health-check queue name for Service Bus

Without `HealthCheckQueueName` the Service Bus health check verifies only that a client could be constructed. Pointing it at the queue the system actually uses makes it probe a real receiver.

### Accept eager validation

The integrations validate connection configuration at registration, so a misconfigured service now fails at startup instead of on first use. This is strictly better — it is what exposed the scheduler gap — but it makes the AppHost wiring load-bearing: every service calling `AddInfrastructure` needs all three references, even ones it does not use. `AddInfrastructure` is a single shared module, so the AppHost now gives all workers `tables`, `blobs` and `servicebus`.

Considered and rejected: splitting `AddInfrastructure` into per-resource modules so the scheduler could take only what it needs. That is a larger refactor whose benefit is avoiding two unused connection references, and it would fragment a deliberately single entry point.

## Risks / Trade-offs

- **An `AZURE_CLIENT_ID` supplied only through a non-environment configuration source would no longer pin the managed identity** → No environment does this; Terraform injects it as a container-app environment variable. Recorded as **BREAKING** in the proposal so the constraint is explicit rather than assumed.

- **`/health` can now fail for reasons it previously ignored** → Intended: it now reflects dependency health rather than process liveness. The cost is that a transient Storage or Service Bus blip can mark a service unhealthy, which for an ACA app can affect routing. Accepted; a genuinely unreachable dependency is worth reporting.

- **The default `/health` writer reports only a status string**, so identifying which check failed required reading the service's logs → Left as is. A detailed response writer would help, but exposing per-dependency failure detail on an HTTP endpoint deserves its own decision about non-development environments.

- **Every service now carries connection references it may not use** (the scheduler never touches blobs) → Accepted as the cost of one shared infrastructure module; the alternative is per-resource registration modules.

- **The Azurite `--skipApiVersionCheck` flag is a workaround for an emulator version lag** → It only affects the local emulator, never deployed Storage. If a future Azurite understands the SDK's `x-ms-version`, the flag becomes a harmless no-op.
