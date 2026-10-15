# Auto Dependency Remediator

Automatically updates dependencies across configured Azure DevOps repositories, opens
pull requests, and runs an AI loop (Microsoft Agent Framework + Ollama) to
address breaking changes the updates introduce. Runs are timed, queued, and processed
by horizontally scalable workers. A React web UI configures the tool and shows status.

## Solution layout

```
AutoRemediator.sln
src/
  AutoRemediator.AppHost            # .NET Aspire orchestrator (composes everything)
  AutoRemediator.ServiceDefaults    # OpenTelemetry, health checks, service discovery
  AutoRemediator.Api                # Minimal API, vertical slice (endpoints -> handlers, no mediator)
  AutoRemediator.Web                # React + TypeScript SPA (Vite, shadcn/ui + Tailwind, TanStack Table)
  AutoRemediator.Worker.Scheduler   # Run-once trigger -> enqueues remediation runs
  AutoRemediator.Worker.Remediation # Queue consumer -> runs the remediation/AI loop
  AutoRemediator.Agents             # Microsoft Agent Framework agents (Ollama)
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
`Agents`). No library references a host. The web app is not a .NET project: it talks to the API
over HTTP only, through same-origin `/api/...` URLs.

## Prerequisites

- .NET 10 SDK (built with `10.0.300`)
- .NET Aspire templates (`dotnet new install Aspire.ProjectTemplates`) — 13.4.6
- The Aspire CLI (`dotnet tool install -g Aspire.Cli`) — 13.4.6. Use it to start and stop
  the app; see [Run locally](#run-locally-aspire).
- Node.js 24 with npm, for the web app. The AppHost runs `npm install` for it on start.
- A container runtime (Docker Desktop or Podman) **running**, to start the local
  Azurite (Storage) and Azure Service Bus emulators when you run the AppHost.
- [Ollama](https://ollama.com) — **only needed to exercise the AI remediation loop**, and
  there is nothing to install: the AppHost runs it as a container, so the container runtime
  above is enough. The first start pulls `phi4` (several GB) into the `ollama` data volume.
  Set `Model__Enabled=false` before starting to skip it. The model is a *soft* dependency:
  without it the rest of the system runs normally and still opens pull requests, and only
  AI repair of compile breaks is unavailable.

## Build & test

```bash
dotnet build AutoRemediator.sln
dotnet test  AutoRemediator.sln     # Playwright functional test self-skips (no app running)

cd src/AutoRemediator.Web
npm install
npm run lint && npm run typecheck && npm run build
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

The web app runs on the Vite dev server. The AppHost passes it the API's address, and Vite
proxies `/api` to it, so the browser sees a single origin, as it does when deployed. To work on
the UI alone against an API you already have running, set that address yourself:
`services__api__http__0=http://localhost:<port> npm run dev`.

Use the Aspire CLI rather than `dotnet run` on the AppHost: `dotnet run` bypasses the
CLI's lifecycle management and leaves orphaned processes holding file locks on `bin/` and
`obj/`. If a build fails with `MSB3491` or `CS2012`, the app is still running — `aspire
stop` releases the locks.

### Local repository mode

To run against plain git repositories on disk instead of Azure DevOps (no PAT, no Azure DevOps
access), set two environment variables and start the AppHost:

```powershell
$env:LocalRepos__Enabled='true'; $env:Model__Enabled='false'; aspire start
aspire resource scheduler start     # once api is running and local-scenarios-enroll has finished
```

Nothing needs generating or enrolling by hand: the AppHost builds the scenario feed and demo repos
under `.local` when they are missing or stale (about 85 s cold, a few seconds when current),
enrolls them through the API (a global baseline plus each repo's own settings), and gives `api`, `scheduler` and
`remediation` `AzureDevOps__Mode=Local`. The scheduler is not automatic: start it when you want a
pass. Other variables: `LocalRepos__Root` (default `.local`), `LocalRepos__ResetOnStart` (default `true`)
and `LocalRepos__RunOnStart` (default `false`; `true` starts the scheduler automatically). To reset
the repos between runs without a rebuild, start the `local-scenarios-reset` resource
(`aspire resource local-scenarios-reset start`).

With `Model__Enabled=false`, remediation is non-agentic: a build that fails after the bump ends
`VerificationFailed` with no PR. See [docs/local-testing.md](docs/local-testing.md) for the full
walkthrough, and [tools/local-scenarios/README.md](tools/local-scenarios/README.md) for the scripts.

### Functional (Playwright) smoke test

With the app running, take the Web URL from the Aspire dashboard and run:

```bash
# one-time browser install
pwsh tests/AutoRemediator.Web.FunctionalTests/bin/Debug/net10.0/playwright.ps1 install
WEB_BASE_URL=<web-url> dotnet test tests/AutoRemediator.Web.FunctionalTests
```

`AccessibilityTests` runs axe-core (WCAG 2.0/2.1/2.2 A and AA, via the `Deque.AxeCore.Playwright`
NuGet package) over every route in light and dark, plus the mobile navigation drawer at 375px. The
run-detail case uses the first run from `/api/runs` (the unknown-run page if none exists). To cover
the review panel too, run `ReviewGateTests` first (it seeds a held run) with `ConnectionStrings__tables`
and `ConnectionStrings__blobs` pointing at the running Azurite.

axe finds only the machine-checkable part of WCAG. Before a release that changes the UI, also check
manually:

- [ ] Keyboard only: every page reachable and operable, focus always visible, no traps outside the drawer.
- [ ] 200% browser zoom at 1280px: no clipped or overlapping content.
- [ ] 320px width: no horizontal page scroll (tables scroll inside their own region).
- [ ] Text spacing override (WCAG 1.4.12 bookmarklet): no clipped text.
- [ ] Reduced motion on: nothing animates.
- [ ] Windows high contrast / forced colors: focus rings and pill labels remain visible.

## Web app — local vs. deployed

Locally the SPA is served by the Vite dev server. Deployed, `src/AutoRemediator.Web/Dockerfile`
builds it with Node and serves the static files from nginx on port 8080 (build from the repository
root like the other images). nginx also reverse-proxies `/api` to the API at the `API_URL`
environment variable, read at container start, e.g. `API_URL=https://<api-fqdn>` with no trailing
slash.

## Worker execution models — local vs. deployed

The workers run as plain project hosts locally, but their **deployed** shape differs and
should not be inferred from the local shape:

| Worker | Deployed as | Trigger | Behavior |
| --- | --- | --- | --- |
| `Worker.Scheduler` | Scheduled **Azure Container Apps Job** | Cron tick every 15 minutes (in the Job) | Runs once per tick: enumerate enabled repos, enqueue one `RemediationRunRequested` only for each repo that is due (default cadence daily at 00:00 UTC, or the repo's or global default schedule), at most one run per repo per tick, with schedule state in the `schedulestate` table, then **exit**. No in-process timer. A dev-only loop (`Scheduler:DevLoopEnabled`) can re-trigger it locally. |
| `Worker.Remediation` | Event-driven **Azure Container Apps Job** | Service Bus queue depth (KEDA) | Scales out per queued message, processes the run via the agent, scales to zero when idle. |

The ACA Job definitions and KEDA/cron settings live in Terraform — see
[`infra/terraform`](infra/terraform/README.md).

## The AI remediation loop — local vs. deployed

When a dependency bump verifies cleanly, nothing AI-related happens. When it fails to
**compile**, the run hands the diagnostics and the affected source to an agent, applies the
edits it proposes, and re-verifies — up to a bounded number of attempts. A restore-time
(`NU`) conflict never reaches the agent: that is version math, handled by feed-metadata
alignment, and source edits cannot fix it.

The model differs by environment, and **its effectiveness at this task differs with it**:

| | Local development | Deployed |
| --- | --- | --- |
| Model | Ollama (`phi4`), started by the AppHost unless `Model:Enabled=false` | To be decided: the Terraform still provisions Azure AI Foundry, which the Ollama client cannot call (see the [feature risk](.work/features/002-local-repo-mode/FEATURE.md#risks-and-open-questions)) |
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

With the model disabled or unreachable the system runs normally and still opens pull requests; only
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
| `ConnectionStrings:chat` | Injected by the AppHost — the Ollama model the AI loop uses, in the form `Endpoint=http://localhost:11434;Model=phi4` (the host port is assigned by Aspire; see `aspire describe`). Absent means no AI repair. |
| `Model:Enabled` | AppHost setting, default `true` (a missing or unparseable value counts as true). `false` (env var `Model__Enabled`) leaves the `ollama` and `chat` resources out, so `remediation` gets no `ConnectionStrings:chat` and AI repair is unavailable. |
| `LocalRepos:Enabled` | AppHost setting, default `false` (a missing or unparseable value counts as false). `true` (env var `LocalRepos__Enabled`) sets `AzureDevOps__Mode=Local`, `AzureDevOps__LocalRoot=<root>\repos` and `AzureDevOps__LocalPullRequestsRoot=<root>\prs` on `api`, `scheduler` and `remediation`. Independent of `Model:Enabled`. |
| `LocalRepos:Root` | AppHost setting, default the repository's `.local` folder. Root of the `repos` and `prs` folders used in local repository mode. |
| `LocalRepos:ResetOnStart` | AppHost setting, local mode only, default `true` (a missing or unparseable value counts as true). On every start the `local-scenarios` resource also resets the scenario repos and `<root>\prs` to a clean state (no rebuild). `false` (env var `LocalRepos__ResetOnStart`) skips the reset. |
| `LocalRepos:Profile` | Obsolete and ignored (env var `LocalRepos__Profile`): each scenario repo carries its own settings. A non-blank value only prints a notice at AppHost startup. |
| `LocalRepos:RunOnStart` | AppHost setting, local mode only, default `false` (a missing or unparseable value counts as false). In local mode `scheduler` is explicit-start: start it from the dashboard or `aspire resource scheduler start`. `true` (env var `LocalRepos__RunOnStart`) starts it automatically once provisioning has finished. |
| `Agents:MaxAttempts` / `Agents:TokenBudget` / `Agents:AttemptTimeout` | Bounds on the AI loop: attempts per run (3), tokens per run (120k), and per-model-call timeout (3m). |
| `Scheduler:DevLoopEnabled` / `Scheduler:DevLoopIntervalSeconds` | Local-only re-trigger of the run-once scheduler. |
| `AzureDevOps:Mode` | `AzureDevOps` (default) or `Local`, case-insensitive. `Local` swaps the REST client for `LocalRepositoryClient`, which works against plain git repositories on disk (needs `git` on PATH; no PAT). |
| `AzureDevOps:LocalRoot` | Local mode only, required: folder holding one git repository per managed repository `Name` (organization and project are ignored). |
| `AzureDevOps:LocalPullRequestsRoot` | Local mode only: folder for pull request records (`<root>/<repo>/<id>.json`). Defaults to a `prs` folder beside `LocalRoot`. |
| `AzureDevOps:Pat` | Personal access token. Needs **Code (Read & Write)** plus pull-request contribution — approving a held proposal pushes a branch and opens a pull request, so a read-only token is not enough. |

## Recommended agent skills

This repo doesn't ship a fixed set of AI-agent skills — `.claude/skills/`, `.claude/agents/`,
`.agents/skills/`, and `.agents/agents/` are gitignored so each developer can install their own
without churning the repo. If you're using Claude Code (or another skill-compatible agent) against
this codebase, these are worth adding:

- **aspire** / **aspire-orchestration** / **aspire-init** / **aspire-deployment** /
  **aspire-monitoring** — this solution is a .NET Aspire app; these cover starting/stopping the
  AppHost, scaffolding, deployment, and log/trace inspection without falling back to raw `dotnet
  run` (which leaves orphaned processes holding file locks — see [Run locally](#run-locally-aspire)).
- **playwright-cli** — drives the Playwright functional smoke test against a running Web app.
