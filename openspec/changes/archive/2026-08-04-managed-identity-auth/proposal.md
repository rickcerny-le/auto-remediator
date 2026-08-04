## Why

The Terraform IaC deploys the apps with a user-assigned Managed Identity and RBAC, injecting Azure resource **endpoints** (not connection strings) plus `AZURE_CLIENT_ID`. But `AutoRemediator.Infrastructure` builds its Azure clients only from **connection strings**, so the deployed apps cannot authenticate. This change closes that gap while keeping local Aspire/emulator development working unchanged.

## What Changes

- Update `AutoRemediator.Infrastructure` to construct `TableServiceClient`, `BlobServiceClient`, and `ServiceBusClient` in **dual mode**:
  - When the configured value is a **service endpoint** (an `http(s)` URI for Storage, or a bare namespace FQDN for Service Bus), build the client with that endpoint + a token credential (Managed Identity).
  - When the value is a **connection string** (e.g. the local Azurite / Service Bus emulator values injected by Aspire), keep using the connection-string constructor.
- Add a `DefaultAzureCredential` that honors `AZURE_CLIENT_ID` (the user-assigned identity's client id) via `ManagedIdentityClientId`, so the deployed apps select the correct identity.
- Add the `Azure.Identity` package to `AutoRemediator.Infrastructure`.
- Add unit coverage for the endpoint + Managed-Identity construction path (alongside the existing connection-string path).

Non-goals: changing the Agents/MAF client (still a placeholder that makes no calls); removing connection-string support (needed for local emulators); any Terraform change (the IaC already provisions the identity, roles, and endpoint env vars).

## Capabilities

### New Capabilities
- `managed-identity-auth`: How the application authenticates to Azure Storage and Service Bus — endpoint + `DefaultAzureCredential` (Managed Identity) when given endpoints, with connection-string fallback for local emulators, selecting the user-assigned identity via `AZURE_CLIENT_ID`.

### Modified Capabilities
<!-- None — this adds a new application-auth capability; it does not change existing solution-scaffold / service-orchestration / testing-foundation requirements. -->

## Impact

- Modifies `AutoRemediator.Infrastructure` (client construction + `AddInfrastructure`) and adds the `Azure.Identity` dependency; no other project changes.
- Unblocks the deployed apps provisioned by `add-terraform-iac` — they can now authenticate via Managed Identity using the injected endpoints.
- Local development is unaffected: Aspire keeps injecting emulator connection strings, which continue to take the connection-string path.
