# Auto Dependency Remediator

Automatically updates dependencies across configured Azure DevOps repositories, opens
pull requests, and runs an AI loop (Microsoft Agent Framework + Azure AI Foundry) to
address breaking changes the updates introduce. Runs are timed, queued, and processed
by horizontally scalable workers. A Blazor web UI configures the tool and shows status.

> This repository is currently a **scaffold** — the projects, boundaries, orchestration,
> and test topology are in place; remediation, Azure DevOps, and AI-agent logic land in
> later changes.

## Solution layout

```
AutoRemediator.sln
src/
  AutoRemediator.AppHost            # .NET Aspire orchestrator (composes everything)
  AutoRemediator.ServiceDefaults    # OpenTelemetry, health checks, service discovery
  AutoRemediator.Api                # Minimal API, vertical slice (endpoints -> handlers, no mediator)
  AutoRemediator.Web                # Blazor Web App (server)  — InteractiveAuto
  AutoRemediator.Web.Client         # Blazor Web App (WASM client)
  AutoRemediator.Worker.Scheduler   # Run-once trigger -> enqueues remediation runs
  AutoRemediator.Worker.Remediation # Queue consumer -> runs the remediation/AI loop
  AutoRemediator.Agents             # Microsoft Agent Framework agents (Azure AI Foundry)
  AutoRemediator.Domain             # Domain entities/logic
  AutoRemediator.Contracts          # Messages + DTOs shared across services
  AutoRemediator.Shared             # Cross-cutting primitives (Result, guards, base types)
  AutoRemediator.Infrastructure     # Azure Storage / Service Bus / Azure DevOps abstractions
tests/
  AutoRemediator.Domain.Tests
  AutoRemediator.Infrastructure.Tests
  AutoRemediator.Agents.Tests
  AutoRemediator.Api.Tests          # Integration (WebApplicationFactory)
  AutoRemediator.Workers.Tests
  AutoRemediator.Web.FunctionalTests # Playwright (skipped unless WEB_BASE_URL is set)
```

Reference topology flows inward toward the domain: `Domain`/`Contracts` depend only on
`Shared`; `Infrastructure` and `Agents` depend on `Domain`/`Contracts`/`Shared`; hosts
depend on `Infrastructure`/`Contracts`/`ServiceDefaults` (the remediation worker also on
`Agents`). No library references a host.

## Prerequisites

- .NET 10 SDK (built with `10.0.300`)
- .NET Aspire templates (`dotnet new install Aspire.ProjectTemplates`) — 13.4.6
- The Aspire CLI (`dotnet tool install -g Aspire.Cli`) — 13.4.6. Use it to start and stop
  the app; see [Run locally](#run-locally-aspire).
- A container runtime (Docker Desktop or Podman) **running**, to start the local
  Azurite (Storage) and Azure Service Bus emulators when you run the AppHost.
- [Foundry Local](https://learn.microsoft.com/azure/ai-foundry/foundry-local/get-started)
  — **only needed to exercise the AI remediation loop.** The AppHost models the model
  resource so it runs locally in development; deployed environments use the provisioned
  Azure AI Foundry account instead. The model is a *soft* dependency: without Foundry
  Local the rest of the system runs normally and still opens pull requests, and only
  AI repair of compile breaks is unavailable.

## Build & test

```bash
dotnet build AutoRemediator.sln
dotnet test  AutoRemediator.sln     # Playwright functional test self-skips (no app running)
```

## Run locally (Aspire)

```bash
aspire start          # starts in the background and prints the dashboard URL
aspire describe       # per-resource state and health
aspire stop           # also the fix for file locks (MSB3491 / CS2012) during a build
```

This starts the Aspire dashboard and provisions the Azurite Storage emulator (Tables +
Blobs), the Service Bus emulator, and the local model, then launches the API, Web, and
both workers with their connection information injected — no connection strings are
hard-coded in project code. A running container runtime is required.

Use the Aspire CLI rather than `dotnet run` on the AppHost: `dotnet run` bypasses the
CLI's lifecycle management and leaves orphaned processes holding file locks on `bin/` and
`obj/`. If a build fails with `MSB3491` or `CS2012`, the app is still running — `aspire
stop` releases the locks.

### Functional (Playwright) smoke test

With the app running, take the Web URL from the Aspire dashboard and run:

```bash
# one-time browser install
pwsh tests/AutoRemediator.Web.FunctionalTests/bin/Debug/net10.0/playwright.ps1 install
WEB_BASE_URL=<web-url> dotnet test tests/AutoRemediator.Web.FunctionalTests
```

## Worker execution models — local vs. deployed

The workers run as plain project hosts locally, but their **deployed** shape differs and
should not be inferred from the local shape:

| Worker | Deployed as | Trigger | Behavior |
| --- | --- | --- | --- |
| `Worker.Scheduler` | Scheduled **Azure Container Apps Job** | Cron schedule (in the Job) | Runs once: enumerate enrolled repos, enqueue one `RemediationRunRequested` each, then **exit**. No in-process timer. A dev-only loop (`Scheduler:DevLoopEnabled`) can re-trigger it locally. |
| `Worker.Remediation` | Event-driven **Azure Container Apps Job** | Service Bus queue depth (KEDA) | Scales out per queued message, processes the run via the agent, scales to zero when idle. |

The ACA Job definitions and KEDA/cron settings themselves are deferred to a later
deployment/IaC change.

## Configuration

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:tables` / `:blobs` / `:servicebus` | Injected by the AppHost (Azurite / SB emulator locally). |
| `Agents:FoundryEndpoint` / `Agents:ModelDeploymentName` | Azure AI Foundry endpoint + model deployment for the MAF agent. |
| `Scheduler:DevLoopEnabled` / `Scheduler:DevLoopIntervalSeconds` | Local-only re-trigger of the run-once scheduler. |
