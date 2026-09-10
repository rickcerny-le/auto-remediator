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

## The AI remediation loop — local vs. deployed

When a dependency bump verifies cleanly, nothing AI-related happens. When it fails to
**compile**, the run hands the diagnostics and the affected source to an agent, applies the
edits it proposes, and re-verifies — up to a bounded number of attempts. A restore-time
(`NU`) conflict never reaches the agent: that is version math, handled by feed-metadata
alignment, and source edits cannot fix it.

The model differs by environment, and **its effectiveness at this task differs with it**:

| | Local development | Deployed |
| --- | --- | --- |
| Model | Foundry Local (e.g. Phi-4), started by the AppHost | Azure AI Foundry deployment (e.g. `gpt-4o-mini`) |
| Purpose | exercising the loop — bounds, edit safety, transcripts, terminal states | actually repairing breaks |
| Expected repair rate | **low; do not use it to judge the feature** | the number that matters, and it is measured, not assumed |

The loop's mechanics are deterministic and fully tested against a fake chat client. **No test
asserts a repair rate**, because that would be asserting a property of a model rather than of
this system. Treat a local run that fails to repair as normal.

Failing to repair is never worse than not trying: the run ends exactly where the same
rejection ends with no agent at all — `VerificationFailed`, no pull request — plus the attempt
count and a transcript.

Guardrails worth knowing:

- The agent proposes edits; it never runs commands. Only `restore` and `build` execute.
- Edits are refused unless they target an existing file inside the run's temporary tree.
  Dependency manifests, project files, lock files and verification artifacts are off limits,
  so the agent cannot override version selection.
- Nothing is ever pushed outside a pull request on `autoremediator/dependency-updates`.
- A pull request containing agent edits **says so**, with the attempt count and a transcript
  link. Review those diffs accordingly.

Without Foundry Local installed the system runs normally and still opens pull requests; only
AI repair is unavailable, and runs record that rather than implying an attempt.

## The in-app review gate

A change the AI loop repaired is never pushed automatically. When the loop's edits produce a
change that verifies, the run rests in `AwaitingReview` instead of advancing to a push: the
proposal — before/after content for every changed file, the diagnostics that provoked the repair,
the attempt count, and the verification time — is stored, and a person decides from the run detail
page. A mechanical bump that verified without the AI loop is unaffected and still pushes and opens
its pull request in the same run, exactly as before this feature existed.

Four commands are available from a held run: **Approve** (push the stored proposal and open the
pull request, no re-verification), **Rebuild** (re-verify at the branch's current head, replacing
the proposal on success), **Retry** (run the AI loop again from the original diagnostics, dropping
the previous agent edits, spending a fresh token budget), and **Discard** (reject the proposal;
nothing is pushed). A stale approval — the update branch moved since the proposal was verified — is
refused and reported as recoverable, with Rebuild as the remedy; it is never reported as a failure.

**A held repository receives no new scheduled runs.** The scheduler skips a repository with an open
proposal and records the skip as a `SkippedHeld` run, so a held repository reads as held — visible
on the Web app's overview page and in the runs list — rather than as merely quiet.

**Local vs. deployed shape of the review-command consumer.** Locally, the worker that executes
review commands (`ReviewCommandWorker`) runs as a second `BackgroundService` inside the same
`AutoRemediator.Worker.Remediation` process as the scheduled-run consumer. Deployed, that process
is an event-driven Container Apps Job scaled by KEDA on Service Bus queue depth; the review-command
queue (`review-commands`) is a second scale rule on that same Job, so a queued command wakes a
replica that scaled to zero exactly as a scheduled run does. This is the one place this feature's
deployed shape differs from its local shape.

**The approve control is unauthenticated.** The app has no authentication yet, so anyone who can
reach the Web app can approve a change that pushes to the target repository. This is accepted for
local use; authentication is planned before this is exposed in a deployed environment, and this
caveat should not be treated as resolved by anything in this feature.

## Configuration

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:tables` / `:blobs` / `:servicebus` | Injected by the AppHost (Azurite / SB emulator locally). |
| `ConnectionStrings:chat` | Injected by the AppHost — the model the AI loop uses (Foundry Local locally, the Foundry deployment when deployed). Absent means no AI repair. |
| `Agents:MaxAttempts` / `Agents:TokenBudget` / `Agents:AttemptTimeout` | Bounds on the AI loop: attempts per run (3), tokens per run (120k), and per-model-call timeout (3m). |
| `Scheduler:DevLoopEnabled` / `Scheduler:DevLoopIntervalSeconds` | Local-only re-trigger of the run-once scheduler. |
| `AzureDevOps:Pat` | Personal access token. Needs **Code (Read & Write)** plus pull-request contribution — approving a held proposal pushes a branch and opens a pull request, so a read-only token is not enough. |
