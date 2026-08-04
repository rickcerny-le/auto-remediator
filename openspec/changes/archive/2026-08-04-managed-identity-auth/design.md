## Context

`AutoRemediator.Infrastructure.AddInfrastructure` registers `TableServiceClient`, `BlobServiceClient`, and `ServiceBusClient` as singletons built from `config.GetConnectionString(...)` values. Locally, Aspire injects emulator **connection strings** under those keys. The `add-terraform-iac` change instead injects Azure **service endpoints** (Storage table/blob URIs, Service Bus namespace FQDN) plus `AZURE_CLIENT_ID`, expecting the app to authenticate via the user-assigned Managed Identity. The two worlds share the same config keys but carry different value shapes.

## Goals / Non-Goals

**Goals:**
- Same config keys work in both worlds: connection strings locally, endpoints + Managed Identity in Azure.
- Select the user-assigned identity via `AZURE_CLIENT_ID`.
- No network calls at registration or in tests; local dev unchanged.

**Non-Goals:**
- Changing the Agents/MAF client (placeholder, no calls yet).
- Removing connection-string support.
- Any Terraform or other project change.

## Decisions

**1. Discriminate by value shape, not by environment.**
- Storage: if the value parses as an absolute `http`/`https` URI → endpoint + credential; else → connection string. (`UseDevelopmentStorage=true` is not a URI → connection string.)
- Service Bus: if the value carries connection-string markers (`Endpoint=`, `SharedAccessKey`, `SharedAccessSignature`) → connection string; else treat as a bare namespace FQDN → FQDN + credential.
Rationale: robust and self-describing; no `IsDevelopment()` branching or extra flags. *Alternative:* an explicit `UseManagedIdentity` flag — more config surface, easy to set wrong; rejected.

**2. One `DefaultAzureCredential`, pinned by `AZURE_CLIENT_ID`.**
Build a single `DefaultAzureCredential` with `ManagedIdentityClientId = config["AZURE_CLIENT_ID"]` when that value is present, and reuse it across all endpoint-based clients. Rationale: `DefaultAzureCredential` works for local `az login`/VS and for the deployed user-assigned MI; pinning the client id disambiguates when multiple identities are available. *Alternative:* `ManagedIdentityCredential` only — wouldn't cover local developer credentials; rejected.

**3. Keep the manual registration and `ITableStore`/`IBlobStore`/messaging wrappers.**
Only the client-construction expressions change; the abstractions, DI shape, and `AddInfrastructure` signature stay the same. Rationale: minimal blast radius; existing tests for the wrappers keep holding.

**4. Add `Azure.Identity`.**
`DefaultAzureCredential` / `TokenCredential` come from `Azure.Identity` / `Azure.Core`. Add `Azure.Identity` to `AutoRemediator.Infrastructure` (the Azure SDK clients already provide the endpoint+credential constructors).

## Risks / Trade-offs

- **[Wrong value-shape classification]** → A malformed endpoint could be misread as a connection string (or vice versa). Mitigation: strict `Uri.TryCreate` absolute-http check for Storage and explicit marker check for Service Bus; unit tests cover both shapes.
- **[Credential not exercised in tests]** → Constructing a client with an endpoint + credential makes no network call, so tests verify construction/resolution only, not live auth. Mitigation: acceptable; live auth is validated at deploy time against the provisioned RBAC.
- **[Local emulator regression]** → The emulator connection strings must still take the connection-string path. Mitigation: existing connection-string test retained; `UseDevelopmentStorage=true` and `Endpoint=sb://...` both classify as connection strings.

## Migration Plan

Pure code change; no data or infra migration. Rolls out with the next image build. Rollback is reverting the Infrastructure change (local dev is unaffected either way).

## Open Questions

- None blocking. If a future non-Azure environment needs a different credential chain, the single `CreateCredential` helper is the seam to adjust.
