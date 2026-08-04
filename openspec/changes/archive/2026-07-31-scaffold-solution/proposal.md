## Why

The Auto Dependency Remediator has no codebase yet. Before any feature work (dependency scanning, PR creation, the AI breaking-change loop) can begin, we need a solution skeleton that establishes the microservice boundaries, the Aspire orchestration, the shared libraries, and the testing conventions. Getting this foundation right up front means every later vertical slice drops into a consistent, already-wired structure instead of accreting ad hoc.

## What Changes

- Create the `AutoRemediator.sln` solution with a `src/` + `tests/` layout and a root namespace of `AutoRemediator`.
- Stand up **.NET Aspire** orchestration: `AutoRemediator.AppHost` and `AutoRemediator.ServiceDefaults`.
- Scaffold the **API** (`AutoRemediator.Api`) as a minimal-API host organized by vertical slice, where endpoints call feature handlers directly via DI (no mediator library).
- Scaffold the **UI** (`AutoRemediator.Web` + `AutoRemediator.Web.Client`) as a Blazor Web App using the **Auto** (InteractiveAuto) render mode.
- Scaffold the background services aligned to their deployed Azure Container Apps model:
  - `AutoRemediator.Worker.Scheduler` — a **run-once-and-exit** host deployed as a **scheduled (cron) ACA Job**: enumerate configured repos, enqueue remediation-run messages, then exit.
  - `AutoRemediator.Worker.Remediation` — a queue-processing host deployed as an **event-driven ACA Job** scaled by Service Bus queue depth (KEDA): consume a remediation-run message, run the update + AI breaking-change loop, exit.
- Scaffold `AutoRemediator.Agents` housing the **Microsoft Agent Framework (MAF)** agents, tools, and orchestration (wired to Azure AI Foundry via configuration); consumed by `Worker.Remediation`. Placeholder registration only in this change — no real agent logic.
- Scaffold shared libraries: `AutoRemediator.Shared` (cross-cutting primitives — Result types, guards, base abstractions), `AutoRemediator.Domain` (entities/domain logic), and `AutoRemediator.Contracts` (messages/DTOs shared across services).
- Scaffold `AutoRemediator.Infrastructure` with folders/abstractions for Azure Storage (Tables + Blobs), Azure Service Bus, and an Azure DevOps client — abstractions and DI registration only, no business logic.
- Wire all resources through the AppHost (Storage, Service Bus, Api, Web, and both Workers) so the system runs end-to-end locally via Aspire emulators.
- Create the **test projects** following the stated conventions: xUnit v3 unit + integration test projects for the API, Domain, Infrastructure, Agents, and Workers, plus a Playwright functional test project targeting the Web UI.

Non-goals (deferred to later changes): actual remediation logic, Azure DevOps API calls, real MAF agent implementation, persistence schemas beyond placeholder abstractions, repo-level tooling/CI (`global.json`, `Directory.*.props`, pipelines), and deployment/IaC (the ACA Job definitions themselves).

## Capabilities

### New Capabilities
- `solution-scaffold`: Defines the required solution structure, project set, naming conventions, and project-reference topology for the repository.
- `service-orchestration`: Defines how services are composed and run locally via .NET Aspire (AppHost + ServiceDefaults), which backing resources are registered, and the run-once vs. event-driven worker execution models that mirror their deployed ACA Job form.
- `testing-foundation`: Defines the test project layout and the unit/integration/functional testing conventions (xUnit v3 + Playwright) the repo must satisfy.

### Modified Capabilities
<!-- None — this is the first change; there are no existing specs. -->

## Impact

- Creates the entire initial repository tree (`AutoRemediator.sln`, `src/*`, `tests/*`); no existing code is modified.
- Introduces the baseline dependency surface: .NET 10 SDK, .NET Aspire workload, ASP.NET Core minimal APIs, Blazor Web App, `Microsoft.Extensions.Hosting` workers, Microsoft Agent Framework, Azure Storage / Service Bus client packages, xUnit v3, and Playwright.
- Establishes the conventions (vertical slice, project boundaries, worker execution models, test topology) that all subsequent changes will extend.
- Local run depends on the Aspire-provisioned Azurite (Storage) and Service Bus emulator resources.
