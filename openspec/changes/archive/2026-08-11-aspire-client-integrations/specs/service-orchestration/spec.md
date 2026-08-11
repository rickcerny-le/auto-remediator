## MODIFIED Requirements

### Requirement: Backing resources provisioned locally
The AppHost SHALL provision local backing resources for Azure Storage (via the Azurite emulator, exposing Tables and Blobs) and Azure Service Bus (via the emulator), and SHALL inject their connection information into the services that consume them.

Because client construction is validated at registration, every service that calls `AddInfrastructure` SHALL be given **all** of the Storage and Service Bus connection references — including resources it does not itself use — and SHALL wait for those backing resources before starting. A service missing a reference fails at startup, not on first use.

The storage emulator SHALL be started with `--skipApiVersionCheck`: Azurite rejects the storage SDK's current `x-ms-version` when creating a container, returning a bare 400 that makes blob operations fail locally.

#### Scenario: Services receive resource connection info
- **WHEN** the AppHost starts
- **THEN** the Api and both Workers receive the Storage and Service Bus connection references through Aspire configuration, without hard-coded connection strings in project code

#### Scenario: The scheduler can read its configuration from Table Storage
- **WHEN** the scheduler starts and lists the enrolled repositories
- **THEN** it has the Table Storage connection reference it needs and completes its run rather than failing to construct a client

#### Scenario: Blob operations work against the local emulator
- **WHEN** a service creates a blob container against the storage emulator
- **THEN** the operation succeeds rather than failing with a 400 from an unsupported API version

#### Scenario: Services wait for their backing resources
- **WHEN** the AppHost starts the Api and both Workers
- **THEN** each waits for the Storage and Service Bus resources to become healthy before it starts
