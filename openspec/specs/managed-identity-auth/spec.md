# managed-identity-auth Specification

## Purpose

Defines how `AutoRemediator.Infrastructure` authenticates to Azure services, supporting both Managed Identity (endpoint plus token credential) in deployed environments and connection strings for local emulator development, chosen automatically by the shape of the configured value, while keeping all registrations resolvable and unit-tested without network calls.

## Requirements

### Requirement: Dual-mode Azure client construction
`AutoRemediator.Infrastructure` SHALL construct the Storage (`TableServiceClient`, `BlobServiceClient`) and Service Bus (`ServiceBusClient`) clients from either a service endpoint or a connection string, chosen by the shape of the configured value: an `http`/`https` URI (Storage) or a bare namespace FQDN (Service Bus) SHALL be treated as an endpoint and paired with a token credential; a value carrying connection-string markers SHALL use the connection-string constructor.

#### Scenario: Endpoint value uses a token credential
- **WHEN** `ConnectionStrings:tables`/`:blobs` is an `https` service endpoint and `ConnectionStrings:servicebus` is a bare namespace FQDN
- **THEN** the corresponding clients are constructed from the endpoint plus a token credential (Managed Identity), not a connection string

#### Scenario: Connection-string value keeps working
- **WHEN** the configured value is a connection string (e.g. the local Azurite `UseDevelopmentStorage=true` or a Service Bus emulator `Endpoint=sb://...;SharedAccessKey=...` value)
- **THEN** the corresponding client is constructed from that connection string, preserving local emulator development

### Requirement: User-assigned identity selection
When authenticating by Managed Identity, the application SHALL use a `DefaultAzureCredential` that selects the user-assigned identity indicated by the `AZURE_CLIENT_ID` configuration value when present.

#### Scenario: AZURE_CLIENT_ID pins the managed identity
- **WHEN** `AZURE_CLIENT_ID` is configured and an endpoint-based client is constructed
- **THEN** the credential is configured with that value as its managed-identity client id

#### Scenario: No AZURE_CLIENT_ID still resolves locally
- **WHEN** `AZURE_CLIENT_ID` is absent and all values are connection strings
- **THEN** infrastructure registration still succeeds and the clients resolve (the credential is not exercised)

### Requirement: Registration remains resolvable and unit-tested
`AddInfrastructure` SHALL register the Storage, Service Bus, and Azure DevOps abstractions such that they resolve from the container for both the endpoint and connection-string configurations, and both paths SHALL be covered by unit tests that make no network calls.

#### Scenario: Both configurations resolve the abstractions
- **WHEN** `AddInfrastructure` is called with an endpoint configuration and separately with a connection-string configuration
- **THEN** `ITableStore`, `IBlobStore`, `IMessagePublisher`, `IMessageConsumer`, and `IAzureDevOpsClient` resolve from the service provider in both cases without any network call
