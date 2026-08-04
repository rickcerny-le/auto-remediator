## Context

The Auto Dependency Remediator is a greenfield system: automatically update dependencies across configured Azure DevOps repos, open pull requests, and run an AI loop (Microsoft Agent Framework + Azure AI Foundry) to fix breaking changes the updates introduce. Runs are timed, queued, and processed by horizontally scalable workers. A Blazor UI configures the tool and shows run status.

This change establishes only the **skeleton**: the solution, the Aspire composition, the service boundaries, shared libraries, infrastructure and agent abstractions, and the test topology. It intentionally contains no remediation or agent logic. Its purpose is to make the architecture (vertical slice + microservices) concrete so later slices are additive.

Constraints come from `openspec/config.yaml`: C#/.NET 10, .NET Aspire, Azure (Storage, Service Bus, Container Apps/Jobs, AI Foundry), Microsoft Agent Framework, minimal APIs, worker services, vertical slice architecture, and TDD with xUnit v3 + Playwright. Decisions confirmed with the user: Blazor Web App **Auto** render mode; endpoints-call-handlers directly (no mediator); Azure **Storage-only** persistence for now; `AutoRemediator.*` naming with the shared-primitives library named `AutoRemediator.Shared` (not `Kernel`, to avoid Semantic Kernel confusion); a dedicated `AutoRemediator.Agents` library for MAF; scheduler and remediation workers modeled as **ACA Jobs**; and repo-level tooling/CI deferred.

## Goals / Non-Goals

**Goals:**
- A `dotnet build` / `dotnet test` clean solution with every planned project present as a compiling skeleton.
- Runnable end-to-end locally via the Aspire AppHost with emulated Storage and Service Bus.
- Clear, enforced project-reference topology (dependencies flow inward to Domain).
- A worked example of one vertical slice in the API and one scheduler→worker message path.
- Worker hosts whose process shape matches their deployed ACA Job model (run-once vs. event-driven).
- A dedicated MAF `Agents` seam that `Worker.Remediation` consumes.
- One test per testable project, including a Playwright UI smoke test.

**Non-Goals:**
- Real dependency scanning, PR creation, or Azure DevOps API calls.
- Real MAF agent/orchestration implementation or Azure AI Foundry model calls.
- Persistence schemas beyond placeholder table/blob abstractions.
- Repo tooling (`global.json`, `Directory.*.props`, `.editorconfig`), CI pipelines, IaC, and the ACA Job/deployment definitions themselves.
- Authentication/authorization on the UI or API.

## Decisions

**1. Microservice split: two workers + one API + one UI, mapped to distinct ACA hosting models.**
`Worker.Scheduler` and `Worker.Remediation` are separate hosts, and they deploy differently:
- **Scheduler → scheduled (cron) ACA Job.** It runs to completion: enumerate configured repos, publish a `RemediationRunRequested` message per repo, then exit. The schedule lives in the ACA Job (cron), **not** an in-process timer. In code this is a run-once host — do the work at startup and stop the host (exit code 0) — rather than a perpetual `BackgroundService` loop.
- **Remediation → event-driven ACA Job.** It scales on Service Bus queue depth via KEDA (scale-to-zero when idle, out under load), consuming one/more messages, running the update + AI loop, and exiting.
Rationale: matches "updates kicked off by a job" and "queuing + scale out" from the brief; decouples the timing signal from the process; gives per-workload scaling. *Alternative considered:* long-running ACA Apps with an in-process timer and always-on consumer — simpler but no scale-to-zero and couples scheduling to a live process; rejected.

**2. Local dev models the Job as a project (Aspire has no cron resource).**
Aspire cannot express a scheduled/event Job natively, so locally both workers run as projects under the AppHost. The scheduler executes its work once on startup (with an optional dev-only timer to re-trigger for convenience); the remediation worker runs a live Service Bus consumer locally. The **deployed** form (cron Job / KEDA-scaled Job) is defined later in the IaC/deployment change. This split is documented so no one mistakes the local always-running shape for the production model.

**3. Endpoints call handlers directly (no mediator).**
Each API feature slice under `Features/<Feature>/` owns `Endpoint.cs` + `Handler.cs` + request/response records; the endpoint resolves the handler from DI and calls it. Rationale: avoids the MediatR commercial license and reflection indirection while preserving slice cohesion. *Alternatives:* MediatR (licensing cost), a source-generated mediator, or Wolverine — all add a dependency for indirection we don't need yet.

**4. Aspire owns composition and local resources.**
The AppHost provisions Azurite (Tables + Blobs) and the Service Bus emulator and injects connection references into consumers; `ServiceDefaults` centralizes OpenTelemetry, health checks, and service discovery. Rationale: matches the stated stack and keeps connection strings out of code, easing the later move to real Azure resources. *Alternative:* docker-compose + manual config — more moving parts, no dashboard.

**5. Storage-only persistence.**
`Infrastructure` exposes Table/Blob/Service Bus/Azure DevOps abstractions with a single `AddInfrastructure` DI extension; no EF Core/DbContext this change. Rationale: repo config and run status fit Tables/Blobs and stay serverless-friendly; introduce a relational store later only if query needs demand it.

**6. Blazor Web App with Auto render mode.**
`AutoRemediator.Web` (server) + `AutoRemediator.Web.Client` (WASM) with `InteractiveAuto`. Rationale: server-first responsiveness with client interactivity after the WASM payload loads; shared C# `Contracts` DTOs across API and UI. *Trade-off:* two web projects and a marginally more complex render model, accepted per the user's choice.

**7. Contracts as the shared message/DTO seam.**
Message shapes (e.g. `RemediationRunRequested`) and API DTOs live in `AutoRemediator.Contracts`, referenced by producers, consumers, and the UI. Rationale: one source of truth for cross-service shapes; keeps `Domain` free of transport concerns.

**8. `AutoRemediator.Shared` for cross-cutting primitives — name chosen to avoid Semantic Kernel confusion.**
The shared-primitives library (Result/error types, guards, base abstractions) is named `AutoRemediator.Shared`, not `Shared.Kernel`. Rationale: MAF unifies Semantic Kernel + AutoGen, so a project literally named `Kernel` in an AI codebase is a foot-gun for readers. *Alternative:* `BuildingBlocks` (DDD-idiomatic) — also fine; `Shared` chosen for brevity.

**9. Dedicated `AutoRemediator.Agents` library for MAF.**
Microsoft Agent Framework agents, tools, and orchestration live in `AutoRemediator.Agents`, wired to Azure AI Foundry via configuration and exposed through an `AddAgents` DI extension; `Worker.Remediation` references it. Rationale: keeps the AI concern behind a boundary that the worker (and later the API, for interactive runs) can consume without duplicating MAF wiring. This change adds only a placeholder agent registration — no model calls. *Alternative:* MAF inline in the remediation worker — quicker now, but forces an extraction later and blocks reuse; rejected.

## Risks / Trade-offs

- **[.NET 10 / Aspire / MAF template drift]** → Templates and package versions move fast; pin the SDK, Aspire workload, and MAF package versions during apply and record the exact versions used so the build is reproducible.
- **[Local shape ≠ deployed shape for workers]** → Running Jobs as always-on projects locally can hide run-once/scale-to-zero bugs. Mitigation: keep the scheduler's work in a single idempotent entry method invoked once on startup; add an integration test asserting it completes and exits rather than looping.
- **[Service Bus emulator fidelity]** → The local emulator may not match cloud semantics (sessions, dead-lettering, KEDA scaling). Mitigation: keep messaging behind `Infrastructure` abstractions so cloud specifics can be added without touching workers.
- **[Auto render mode complexity]** → `InteractiveAuto` can surprise with mixed server/client execution and state. Mitigation: keep the initial UI to static + one interactive smoke page; expand deliberately.
- **[Agents seam over-abstraction]** → Building an `Agents` boundary before any agent exists risks a wrong-shaped abstraction. Mitigation: keep it minimal (one interface + placeholder registration) and let the first real remediation slice drive its shape.
- **[Deferred repo tooling]** → Without `Directory.Packages.props`/`global.json`, package/SDK versions can drift across the many projects. Mitigation: note as an explicit follow-up change; keep versions consistent manually until then.

## Migration Plan

Not applicable — greenfield. There is nothing to migrate or roll back; abandoning the change means deleting the created tree. Apply proceeds project-by-project so a partial build failure is localized and correctable before moving on.

## Open Questions

- Exact .NET 10 SDK, Aspire workload, and MAF package versions to pin (resolve at apply time against the installed SDK).
- The cron cadence for the scheduler Job and the KEDA scale rule thresholds for the remediation Job (deferred to the deployment/IaC change).
- Naming of the first example feature slice in the API (placeholder used for now).
