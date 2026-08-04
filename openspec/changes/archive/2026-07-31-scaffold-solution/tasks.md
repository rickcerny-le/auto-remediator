## 1. Solution and folder skeleton

- [x] 1.1 Verify the installed .NET 10 SDK, the .NET Aspire workload, and the Microsoft Agent Framework package versions; record the exact versions used for this scaffold
- [x] 1.2 Create `AutoRemediator.sln` at the repository root
- [x] 1.3 Create the `src/` and `tests/` directories

## 2. Aspire orchestration

- [x] 2.1 Create `src/AutoRemediator.ServiceDefaults` (Aspire service defaults library) with the standard extension wiring OpenTelemetry, health checks, and service discovery
- [x] 2.2 Create `src/AutoRemediator.AppHost` (Aspire app host) and add both to the solution
- [x] 2.3 In the AppHost, provision the Azurite Storage emulator resource (Tables + Blobs)
- [x] 2.4 In the AppHost, provision the Azure Service Bus emulator resource
- [x] 2.5 Confirm the AppHost runs and the Aspire dashboard loads before adding downstream services *(verified via `aspire run` on Podman — dashboard at https://localhost:17075, all resources reached Running)*

## 3. Shared libraries

- [x] 3.1 Create `src/AutoRemediator.Shared` for cross-cutting primitives (Result types, base abstractions, guard helpers)
- [x] 3.2 Create `src/AutoRemediator.Domain` referencing only `Shared`; add a placeholder aggregate (e.g. `ManagedRepository`)
- [x] 3.3 Create `src/AutoRemediator.Contracts` referencing only `Shared`; add the `RemediationRunRequested` message record and a sample API DTO
- [x] 3.4 Add all three to the solution and verify the reference topology (Domain→Shared, Contracts→Shared only)

## 4. Infrastructure

- [x] 4.1 Create `src/AutoRemediator.Infrastructure` referencing `Domain`, `Contracts`, `Shared`
- [x] 4.2 Add Azure Storage client packages and a `Storage/` folder with Table and Blob abstractions (interfaces + thin client wrappers)
- [x] 4.3 Add an Azure Service Bus `Messaging/` folder with publish/consume abstractions
- [x] 4.4 Add an `AzureDevOps/` folder with an Azure DevOps client abstraction (interface + stub implementation, no real calls)
- [x] 4.5 Add a single `AddInfrastructure(this IHostApplicationBuilder)` DI extension registering Storage, Service Bus, and Azure DevOps abstractions
- [x] 4.6 Add the project to the solution and verify it references no host project

## 5. Agents (Microsoft Agent Framework)

- [x] 5.1 Create `src/AutoRemediator.Agents` referencing `Contracts`, `Domain`, `Shared`; add the Microsoft Agent Framework package(s)
- [x] 5.2 Add an `IRemediationAgent` abstraction (placeholder) and a MAF-backed implementation stub wired to Azure AI Foundry settings from configuration (no live model call)
- [x] 5.3 Add an `AddAgents(this IHostApplicationBuilder)` DI extension registering the agent(s) using configuration; make no network call at registration time
- [x] 5.4 Add the project to the solution and verify it references no host project

## 6. API (vertical slice)

- [x] 6.1 Create `src/AutoRemediator.Api` as a minimal-API host referencing `Infrastructure`, `Contracts`, `Domain`, `ServiceDefaults`
- [x] 6.2 Wire `AddServiceDefaults()` and `AddInfrastructure()` in `Program.cs`; map default health check endpoints
- [x] 6.3 Add a `Features/` folder and one example slice `Features/Repositories/List/` with `Endpoint.cs`, `Handler.cs`, and request/response records; endpoint resolves the handler from DI (no mediator)
- [x] 6.4 Add a slice-registration convention (e.g. `MapFeatureEndpoints()`) that maps all feature endpoints
- [x] 6.5 Register the API in the AppHost and confirm it reaches running state *(verified — `api` resource Running; `/health` and `/alive` = 200; `/api/repositories` returns the slice JSON)*

## 7. Web UI (Blazor Web App, Auto)

- [x] 7.1 Create `src/AutoRemediator.Web` (Blazor Web App server) and `src/AutoRemediator.Web.Client` (WASM) with `InteractiveAuto` render mode
- [x] 7.2 Reference `Contracts` and `ServiceDefaults` from `Web`; call `AddServiceDefaults()`
- [x] 7.3 Add a home page and one interactive component demonstrating Auto render mode
- [x] 7.4 Register the Web app in the AppHost with a service-discovery reference to the API and confirm it runs *(verified — `web` resource Running; serves `<h1>Auto Dependency Remediator</h1>` over https:56115 / http:56116)*

## 8. Workers

- [x] 8.1 Create `src/AutoRemediator.Worker.Scheduler` (Worker SDK host) referencing `Infrastructure`, `Contracts`, `ServiceDefaults`; structure it as a **run-once** host that enumerates configured repos, publishes one `RemediationRunRequested` per repo, then stops the host (exit 0) — no perpetual timer loop (cron lives in the deployed ACA Job)
- [x] 8.2 Add an optional dev-only re-trigger timer to the scheduler, guarded by environment, so local Aspire runs can re-invoke the run-once work without changing the production shape
- [x] 8.3 Create `src/AutoRemediator.Worker.Remediation` (Worker SDK host) referencing `Infrastructure`, `Contracts`, `Domain`, `Agents`, `ServiceDefaults`; add a Service Bus consumer that reads `RemediationRunRequested` and delegates to the `IRemediationAgent` placeholder (logs receipt, no real logic)
- [x] 8.4 Call `AddServiceDefaults()`, `AddInfrastructure()`, and (remediation only) `AddAgents()` in the workers; map health checks
- [x] 8.5 Register both workers in the AppHost and confirm the scheduler→worker message flows end-to-end against the emulator *(verified — `remediation-runs` queue Running, `remediation` consumer Running; `scheduler` transitioned Running→Finished cleanly after waiting on Service Bus, i.e. it published then exited per the run-once model)*

## 9. Test projects

- [x] 9.1 Create `tests/AutoRemediator.Domain.Tests` (xUnit v3) with a placeholder test for the sample aggregate
- [x] 9.2 Create `tests/AutoRemediator.Infrastructure.Tests` (xUnit v3) with a test asserting `AddInfrastructure` registers the expected services
- [x] 9.3 Create `tests/AutoRemediator.Agents.Tests` (xUnit v3) with a test asserting `AddAgents` registers the agent abstraction from configuration without a network call
- [x] 9.4 Create `tests/AutoRemediator.Api.Tests` (xUnit v3) with an integration test that boots the API via a test server and calls the example endpoint
- [x] 9.5 Create `tests/AutoRemediator.Workers.Tests` (xUnit v3) with tests asserting (a) the scheduler completes-and-exits having published a message per repo, and (b) the remediation consumer can deserialize `RemediationRunRequested`
- [x] 9.6 Create `tests/AutoRemediator.Web.FunctionalTests` (Playwright) with a smoke test loading the home page and asserting on rendered content
- [x] 9.7 Add all test projects to the solution

## 10. Verification

- [x] 10.1 Run `dotnet build AutoRemediator.sln` and confirm zero errors
- [x] 10.2 Run `dotnet test AutoRemediator.sln` and confirm all placeholder/smoke tests pass (excluding Playwright which needs the app running)
- [x] 10.3 Run the AppHost and confirm Api, Web, and both Workers reach running state in the Aspire dashboard *(verified via `aspire run` on Podman — api/web/remediation Running, scheduler Running→Finished; storage + servicebus emulators Running)*
- [x] 10.4 Run the Playwright functional smoke test against the running Web app and confirm it passes *(verified — Chromium hit https://localhost:56115, asserted heading/tagline/interactive component; 1 passed)*
- [x] 10.5 Add a root `.gitignore` for .NET and a `README.md` documenting how to run the AppHost, the test suite, and the local-vs-deployed worker execution models (scheduler = cron ACA Job / run-once; remediation = KEDA-scaled event ACA Job)
