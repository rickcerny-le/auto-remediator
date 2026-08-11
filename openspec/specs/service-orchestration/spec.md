# service-orchestration Specification

## Purpose

Defines how the AutoRemediator services are composed and run via a .NET Aspire AppHost, how local backing resources are provisioned, how shared service defaults are applied to every host, and the execution models of the scheduler and remediation workers against their deployed Azure Container Apps Job forms.

## Requirements

### Requirement: Aspire AppHost composes all services
The `AutoRemediator.AppHost` project SHALL be a .NET Aspire app host that references and orchestrates the `Api`, `Web`, `Worker.Scheduler`, and `Worker.Remediation` projects so the full system can be launched with a single run.

#### Scenario: Running the AppHost starts the whole system
- **WHEN** a developer runs the `AutoRemediator.AppHost` project
- **THEN** the Aspire dashboard starts and the Api, Web, and both Worker resources reach a running state

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

### Requirement: Shared service defaults applied to every host
Every executable host (`Api`, `Web`, `Worker.Scheduler`, `Worker.Remediation`) SHALL reference `AutoRemediator.ServiceDefaults` and call its default configuration extension so that OpenTelemetry, health checks, and service discovery are consistently applied.

#### Scenario: A host wires up service defaults
- **WHEN** any host process starts
- **THEN** it has invoked the ServiceDefaults extension and exposes the standard health check endpoints

### Requirement: Scheduler is a run-once host deployed as a scheduled ACA Job
The `Worker.Scheduler` SHALL be structured to perform its work once per invocation — enumerate configured repositories and publish a `RemediationRunRequested` message per repository — and then stop the host (exit), rather than running a perpetual timer loop. Its production trigger is a scheduled (cron) Azure Container Apps Job; the cron schedule SHALL live in the ACA Job definition, not as an in-process timer. For local Aspire development it MAY execute its work once on startup (optionally re-triggered by a dev-only timer).

#### Scenario: Scheduler completes and exits
- **WHEN** the scheduler host runs to completion
- **THEN** it has published one `RemediationRunRequested` message per configured repository and the host stops with a success exit code rather than continuing to run

### Requirement: Remediation worker is an event-driven consumer deployed as a scaled ACA Job
The `Worker.Remediation` SHALL be structured as a Service Bus queue consumer that reads `RemediationRunRequested` messages and processes each one. Its production form is an event-driven Azure Container Apps Job scaled by Service Bus queue depth (KEDA). Message shapes SHALL be defined in `AutoRemediator.Contracts`, and remediation execution SHALL be delegated to the `AutoRemediator.Agents` library (placeholder in this change).

#### Scenario: A scheduled trigger produces a message the worker can consume
- **WHEN** the scheduler publishes a `RemediationRunRequested` message defined in `Contracts` to Service Bus
- **THEN** the remediation worker's consumer is registered to receive messages of that type and, on receipt, invokes the Agents abstraction to handle the run

### Requirement: Worker execution model documented against deployment
The repository SHALL document, for each worker, that its local always-running project shape differs from its deployed ACA Job model (scheduler = cron Job that runs-once-and-exits; remediation = KEDA-scaled event Job), so the local shape is not mistaken for the production model.

#### Scenario: Docs state the local-vs-deployed distinction
- **WHEN** the project README (or worker docs) is read
- **THEN** it states that the scheduler deploys as a run-once scheduled ACA Job and the remediation worker deploys as an event-driven, queue-depth-scaled ACA Job
