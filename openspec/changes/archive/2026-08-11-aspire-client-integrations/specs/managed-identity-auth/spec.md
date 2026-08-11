## MODIFIED Requirements

### Requirement: Dual-mode Azure client construction
`AutoRemediator.Infrastructure` SHALL register the Storage (`TableServiceClient`, `BlobServiceClient`) and Service Bus (`ServiceBusClient`) clients through the Aspire client integrations, which resolve each `ConnectionStrings:<name>` value in dual mode: an `http`/`https` URI (Storage) or a bare namespace FQDN (Service Bus) is treated as a service endpoint and paired with a token credential; a value carrying connection-string markers uses the connection string. The system SHALL NOT reimplement that selection itself.

#### Scenario: Endpoint value uses a token credential
- **WHEN** `ConnectionStrings:tables`/`:blobs` is an `https` service endpoint and `ConnectionStrings:servicebus` is a bare namespace FQDN
- **THEN** the corresponding clients are constructed from the endpoint plus a token credential (Managed Identity), not a connection string

#### Scenario: Connection-string value keeps working
- **WHEN** the configured value is a connection string (e.g. the local Azurite `UseDevelopmentStorage=true` or a Service Bus emulator `Endpoint=sb://...;SharedAccessKey=...` value)
- **THEN** the corresponding client is constructed from that connection string, preserving local emulator development

#### Scenario: Missing connection configuration fails at registration
- **WHEN** `AddInfrastructure` is called without a value for a required connection name
- **THEN** the failure surfaces during startup and names the missing connection, rather than being deferred until a client is first resolved

### Requirement: User-assigned identity selection
When authenticating by Managed Identity, the Azure data clients SHALL use the Aspire integrations' own `DefaultAzureCredential`, which selects the user-assigned identity from the `AZURE_CLIENT_ID` **environment variable**. The system SHALL NOT pass a credential into those integrations explicitly, because doing so forces the credential code path even when the configured value is a connection string, leaving the emulator-backed local setup with a Service Bus health check that can never pass. Deployments SHALL therefore supply `AZURE_CLIENT_ID` as a process environment variable.

Credential handling for the Key Vault configuration source is separate and unaffected: it reads the `AZURE_CLIENT_ID` configuration value and pins its own credential.

#### Scenario: AZURE_CLIENT_ID pins the managed identity
- **WHEN** `AZURE_CLIENT_ID` is present in the environment and an endpoint-based client is constructed
- **THEN** the credential resolves the user-assigned identity indicated by that value

#### Scenario: No credential is passed to the integrations
- **WHEN** the Azure clients are registered
- **THEN** no explicit credential is supplied to the integrations, so a connection-string value keeps the connection-string code path

#### Scenario: No AZURE_CLIENT_ID still resolves locally
- **WHEN** `AZURE_CLIENT_ID` is absent and all values are connection strings
- **THEN** infrastructure registration still succeeds and the clients resolve (the credential is not exercised)

### Requirement: Registration remains resolvable and unit-tested
`AddInfrastructure` SHALL register the Storage, Service Bus, and Azure DevOps abstractions such that they resolve from the container for both the endpoint and connection-string configurations, and both paths SHALL be covered by unit tests that make no network calls.

#### Scenario: Both configurations resolve the abstractions
- **WHEN** `AddInfrastructure` is called with an endpoint configuration and separately with a connection-string configuration
- **THEN** `ITableStore`, `IBlobStore`, `IMessagePublisher`, `IMessageConsumer`, and `IAzureDevOpsClient` resolve from the service provider in both cases without any network call

#### Scenario: The Azure clients themselves resolve
- **WHEN** `AddInfrastructure` is called with a connection-string configuration
- **THEN** `TableServiceClient`, `BlobServiceClient` and `ServiceBusClient` resolve from the service provider without any network call

## ADDED Requirements

### Requirement: Each Azure resource contributes a health check
Registering the Storage and Service Bus clients SHALL also register a health check per resource, so a host's health endpoint reflects the reachability of its dependencies rather than only process liveness. The Service Bus registration SHALL name the queue to probe, without which its health check verifies nothing beyond client construction.

#### Scenario: Health checks are registered for every Azure resource
- **WHEN** `AddInfrastructure` is called
- **THEN** the registered health checks include one for Table Storage, one for Blob Storage, and one for Service Bus

#### Scenario: An unreachable dependency is reported as unhealthy
- **WHEN** a configured Azure resource cannot be reached
- **THEN** the host's health endpoint reports unhealthy, while its liveness endpoint continues to report healthy
