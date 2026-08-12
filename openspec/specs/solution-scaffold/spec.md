# solution-scaffold Specification

## Purpose

Defines the structure, naming, project set, and reference topology of the AutoRemediator solution, ensuring a clean-checkout build with source/test separation, inward-flowing dependencies, vertical-slice API organization, and isolation of infrastructure and Microsoft Agent Framework concerns.

## Requirements

### Requirement: Solution structure and naming
The repository SHALL contain a single `AutoRemediator.sln` at the root with all production projects under `src/` and all test projects under `tests/`. Every project SHALL use the root namespace prefix `AutoRemediator` and its folder name SHALL match its project/assembly name. The shared-primitives library SHALL be named `AutoRemediator.Shared` (it SHALL NOT be named `Kernel`, to avoid confusion with Semantic Kernel in this AI codebase).

#### Scenario: Solution builds from clean checkout
- **WHEN** a developer clones the repository and runs `dotnet build AutoRemediator.sln`
- **THEN** the build succeeds with zero errors and every project restores its dependencies

#### Scenario: Layout separates source from tests
- **WHEN** the repository tree is inspected
- **THEN** every production project resides under `src/` and every test project resides under `tests/`, and no project sits at the repository root

### Requirement: Required project set
The solution SHALL include the following projects: `AutoRemediator.AppHost`, `AutoRemediator.ServiceDefaults`, `AutoRemediator.Api`, `AutoRemediator.Web`, `AutoRemediator.Web.Client`, `AutoRemediator.Worker.Scheduler`, `AutoRemediator.Worker.Remediation`, `AutoRemediator.Agents`, `AutoRemediator.Shared`, `AutoRemediator.Domain`, `AutoRemediator.Contracts`, and `AutoRemediator.Infrastructure`.

#### Scenario: All required projects are present
- **WHEN** the solution is loaded
- **THEN** each of the required projects is present and included in `AutoRemediator.sln`

### Requirement: Project reference topology
Project references SHALL flow inward toward the domain and outward-facing hosts SHALL NOT be referenced by libraries. Specifically: `Domain` SHALL depend only on `Shared`; `Contracts` SHALL depend only on `Shared`; `Infrastructure` SHALL depend on `Domain`, `Contracts`, and `Shared`; `Agents` SHALL depend on `Contracts`, `Domain`, and `Shared`; `Api`, `Worker.Scheduler`, and `Worker.Remediation` SHALL depend on `Infrastructure`, `Contracts`, `Domain`, and `ServiceDefaults`; `Worker.Remediation` SHALL additionally depend on `Agents`; `Web` SHALL depend on `Contracts` and `ServiceDefaults`; and no library project SHALL reference any host project (`Api`, `Web`, `AppHost`, or a worker).

#### Scenario: Domain has no outward dependencies
- **WHEN** the `AutoRemediator.Domain` project references are inspected
- **THEN** it references only `AutoRemediator.Shared` and no host, infrastructure, agent, or third-party service SDK

#### Scenario: Remediation worker consumes the agents library
- **WHEN** the `AutoRemediator.Worker.Remediation` project references are inspected
- **THEN** it references `AutoRemediator.Agents` (in addition to `Infrastructure`, `Contracts`, `Domain`, and `ServiceDefaults`)

#### Scenario: No inward reference from hosts into libraries is inverted
- **WHEN** any library project's references are inspected
- **THEN** none of them reference `AutoRemediator.Api`, `AutoRemediator.Web`, `AutoRemediator.AppHost`, or either worker host

### Requirement: Vertical slice organization of the API
The `AutoRemediator.Api` project SHALL organize its code by feature slice under a `Features/` folder, where each feature owns its endpoint mapping and handler in the same folder, and endpoints SHALL invoke feature handlers directly through dependency injection without an intermediary mediator library.

#### Scenario: A feature slice is self-contained
- **WHEN** a feature folder under `Features/` is inspected
- **THEN** it contains the endpoint registration and the handler for that feature co-located, and the endpoint resolves the handler from DI

### Requirement: Infrastructure abstractions without business logic
The `AutoRemediator.Infrastructure` project SHALL expose folders and abstractions for Azure Storage (Tables and Blobs), Azure Service Bus, and an Azure DevOps client, together with a DI registration extension, and SHALL contain no remediation business logic in this change.

#### Scenario: Infrastructure registers its clients via one extension
- **WHEN** a host calls the Infrastructure DI registration extension
- **THEN** the Storage, Service Bus, and Azure DevOps client abstractions are registered in the service container

### Requirement: Microsoft Agent Framework isolated in the Agents library
The `AutoRemediator.Agents` project SHALL house the Microsoft Agent Framework (MAF) agents, tools, and orchestration wiring and SHALL expose a single DI registration extension. The agent SHALL be constructed over a chat client injected from a named connection reference, not from an endpoint and deployment name read from its own configuration section. Registration SHALL make no network call.

The agent's **contract** — the abstraction the run orchestration calls, the diagnostics it is given, and the edits it proposes — SHALL live in the domain, not in the Agents project. `Agents` and `Infrastructure` are siblings: the run orchestration in `Infrastructure` must call the agent, so a contract owned by `Agents` would require project references in both directions, and referencing `Agents` from `Infrastructure` would pull MAF into `Infrastructure`'s transitive closure. Keeping the contract in the domain is what makes this isolation real rather than nominal.

The contract SHALL NOT expose infrastructure types such as the verification workspace; source access SHALL be a narrow abstraction the domain owns, which infrastructure adapts.

#### Scenario: Agents registers MAF wiring via one extension
- **WHEN** a host calls the Agents DI registration extension
- **THEN** the MAF agent abstraction(s) are registered in the service container over the injected chat client, with no network call made at registration time

#### Scenario: MAF does not leak into infrastructure
- **WHEN** the project references are inspected
- **THEN** `Infrastructure` does not reference `Agents`, and neither references the other; both depend on the domain where the agent contract lives

#### Scenario: The contract is free of infrastructure types
- **WHEN** the agent contract is inspected
- **THEN** it names only domain types — the diagnostics, the proposed edits, and a source-reader abstraction — and no verification workspace or client type
