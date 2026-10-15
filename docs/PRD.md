# AutoRemediator — Product Requirements Document

> **Living document.** Originally synthesized from retired capability specs, then revised in a
> PRD-vs-code walkthrough on 2026-10-13. Requirements carry a **Status** line wherever the code
> does not (yet) match; anything without one is implemented as described.

## 1. Problem statement

Teams that consume a shared internal package family (e.g. `Contoso.*`) across many Azure
DevOps repositories fall behind on updates because bumping a dependency is low-value,
easy-to-defer work — until a breaking change accumulates into a large, risky jump. AutoRemediator
keeps repositories continuously current against that package family: it finds outdated matched
packages, bumps them, verifies the result builds, attempts an AI-assisted repair when a bump
breaks compilation, and opens a pull request — holding any AI-authored change for a human decision
before it ever reaches the repository.

The pain is sharpest for developers who **author and consume their own packages** across a
complex, multi-repository estate: every internal release fans out into update work in every
consuming repository, and sibling packages in the family must move together. The same machinery
also serves developers who simply want to **stay current** on the packages they depend on — a
`*` pattern is a supported configuration — but internal package families are the design center.

## 2. Goals

- Keep a configured, explicit set of Azure DevOps repositories current against a configured
  package family, on a schedule or on demand, with no manual triage for the mechanical case.
- Never leave a repository worse off than doing nothing: a change that can't be verified is
  never pushed, and an AI repair attempt that fails is indistinguishable in outcome from never
  having tried.
- Give a person full context to approve, re-verify, retry, or discard any AI-repaired change
  before it reaches the repository.
- Run the entire system locally (Aspire + emulators + a local model) except the one external
  dependency that is the point of the system: the targeted Azure DevOps repositories and their
  package feed.
- Make every run fully observable after the fact: status, diffs, diagnostics, logs, and (when
  applicable) the AI transcript.

**Current focus: production-faithful, run locally.** Everything is built to behave locally
under Aspire exactly as it would deployed — same services, same queues/storage semantics, same
auth model, same settings — so deployment later is a hosting decision, not a rework. *Where* and
*how* it is deployed (pipeline, environments, IaC changes) is deliberately not being decided yet;
existing Terraform is kept but not extended.

## 3. Non-goals (decided)

- **No organization crawl / repository discovery.** Repositories are an explicit allowlist;
  AutoRemediator never discovers new repos on its own.
- **Not a vulnerability/security scanner.** Targeting is package-pattern-first (e.g.
  `Contoso.*`). A `*` pattern is allowed for "stay current on everything," but nothing is
  prioritized by advisories/CVEs, and features that only matter for public-ecosystem bots
  (grouping heuristics, changelog scraping) are not a goal.
- **Azure DevOps only (for now).** Repositories, pushes, and pull requests target Azure DevOps.
  GitHub is a planned future platform (see [§9](#9-future-directions)), so new source-control
  code should sit behind a provider seam rather than spreading ADO specifics further.
- **The run ends at the pull request (for now).** AutoRemediator does not track the PR's CI
  status, review, or merge after opening it. UI must not imply otherwise (e.g. an "Open PRs"
  count that is really "runs that produced a PR").
- **Not a substitute for the target repository's own CI.** Local verification is a fast,
  approximate signal (restore + build only — no tests, analyzers, or custom build targets); the
  repository's own Azure DevOps CI remains the authority on the resulting pull request.
- **The agent never runs commands and never edits version-selection files.** It proposes source
  edits only, inside the existing working tree; manifests, lock files, and verification artifacts
  are off-limits.
- **Nothing is ever pushed outside a pull request**, and never to a protected/target branch
  directly, whether or not an agent contributed.

## 4. Users

- **Package author/platform developer** — owns an internal package family and wants every
  consuming repository to keep up with each release without filing work for each one.
- **Consuming developer** — wants their repositories kept current (internal family or `*`) with
  minimal review effort.
- **Operator** — the person who configures targeted repositories/patterns/policy, watches the
  Runs feed, and reviews/approves AI-repaired changes. Today this is any person who can reach the
  Web app (see [§8, Gap: authentication](#8-decisions--open-questions)).
- **Downstream reviewer** — a repository maintainer who reviews the pull request AutoRemediator
  opens, informed by its verification/AI-authorship disclosure.

## 5. System overview

```
  scheduler (cron tick) ─▶ for each enabled, non-held repo that is due by its schedule ─┐
  operator "Run now" (UI/API) ─▶ one repo ──────────────────────────────────────────────┴▶ enqueue RemediationRunRequested
                                                             │ (Service Bus → remediation job, KEDA)
                                                             ▼
   RUN:  Reading → Analyzing → Applying → Verifying → [Remediating] → Pushing → CreatingPR
     1 READ         pull manifests from Azure DevOps (.csproj, Directory.Packages.props,
                    packages.lock.json) — via REST, no clone
     2 ANALYZE      match package patterns → matched set; resolve policy target from the feed;
                    align intra-family siblings (collateral bumps)
     3 APPLY        edit manifest text for primary + collateral bumps (no push yet)
     4 VERIFY       restore + build the edited tree in a temp workspace (no tests)
        green? ── yes ─▶ push branch, open/refresh PR ─▶ Completed
        └─ CS errors ─▶ AI LOOP (bounded attempts/tokens/timeout) → edits → re-verify
              repaired?  ── yes ─▶ hold for review ─▶ AwaitingReview (see §7 review gate)
              └─ exhausted ─▶ VerificationFailed (no PR)
        └─ NU errors (restore-time only) ─▶ VerificationFailed, agent never invoked
        └─ nothing matched outdated ─▶ NoUpdates (short-circuits before any download)
                                                             ▼
   UI: configure repos+patterns+policy · dependency map · runs feed/detail · review gate
```

Every run reaches exactly one terminal-or-held status: `Completed`, `AwaitingReview`,
`NoUpdates`, `VerificationFailed`, `Failed`, `Discarded`, or (for the scheduler's own bookkeeping)
`SkippedHeld`.

## 6. Functional requirements

Each subsection below is a capability area; this section is a condensed index of *what* each
one guarantees, distilled from specs that have since been retired.

### 6.1 Configuration & targeting
- Persists an explicit, enable/disable-able list of managed repositories (org/project/name +
  target branch) and global targeting settings (package patterns, excludes, feeds, policy, a
  default schedule) in Table Storage, editable via API and the web app's Configuration page.
  Repositories can be edited and enabled or disabled in the UI.
- **Layered settings.** The global settings are a baseline; each repository may carry its own
  settings on top: lists (patterns, excludes, feeds, ignore) are the union of the global list and
  the repository's, global entries first and de-duplicated case-insensitively; scalars (strategy,
  pre-release) are the repository's value when set, otherwise the global one. One **"do not
  inherit"** switch makes a repository use only its own lists, with unset scalars falling back to
  the built-in defaults (Minor, no pre-release); the schedule is excluded from the switch.
- The **effective settings** (the result of that layering) are viewable per repository with the
  source of every value (`Global`, `Repository`, `Both`, `Default`) and any warnings, in the
  repository's detail page and over the API. The whole pipeline (analysis, planning,
  verification, the review commands Rebuild and Retry, and the dependency map) uses the effective
  settings of the repository it works on.

### 6.2 Azure DevOps connectivity
- One authenticated client (PAT from Key Vault in Azure, user-secrets locally) handles reads
  (manifest discovery/content, repo verification, full-tree-as-zip download) and writes (push to
  branch, create-or-find PR) entirely over REST — no `git` clone anywhere in the system.
- A local repository mode (`AzureDevOps:Mode=Local`; needs the `git` binary; development and
  testing only, see `docs/local-testing.md`) swaps this client for plain git repositories on disk
  and is exempt from the no-clone rule above; the Azure DevOps path stays REST-only.

### 6.3 Dependency analysis
- Parses `Directory.Packages.props` and `*.csproj`, matches package ids against configured
  include/exclude globs, resolves each match's status (`Outdated` / `UpToDate` / `Ignored` /
  `Unknown`) against the **policy target** (not the absolute latest), and exposes the result as a
  read-only dependency map in the UI.

### 6.4 Update policy
- Strategy per the effective settings (global, overridable per repository): `Patch`/`Minor`/`Major` selects the highest available version within that
  band relative to the current pin; an ignore-glob list holds matched packages; pre-release
  versions are excluded unless explicitly allowed.

### 6.5 Dependency graph alignment (collateral bumps)
- Reads intra-family dependency ranges from feed metadata (no clone/build) and raises declared
  sibling packages just enough to satisfy those ranges, escalating beyond policy only when
  correctness requires it, iterating to a fixpoint. Only declared packages are touched; undeclared
  transitive dependencies are left to NuGet. Collateral bumps and policy escalations are surfaced
  distinctly in the update set and the PR summary.

### 6.6 Update execution
- Computes the update set (primary + collateral), edits manifest text in place (and carries a
  regenerated `packages.lock.json` when the repo commits one), and defers to local verification
  before anything is pushed. The repository's own CI remains the correctness authority; local
  verification never runs tests.

### 6.7 Local verification
- Downloads the repo tree as a zip (no clone), applies the computed edits, resolves the build
  target (shallowest solution, else every project), runs gated `restore` → `build` with timeouts,
  and classifies the outcome as **Verified** / **DependencyFailure** / **Skipped** — defaulting to
  `Skipped` whenever a failure can't be positively attributed to the dependency change, so an
  environment problem is never mistaken for a real break. Diagnostics and full logs are captured
  as run artifacts; the temp workspace is always deleted, on every exit path.

### 6.8 AI remediation loop
- Invoked only on `CS`-coded (compile) diagnostics — never on restore-time (`NU`) conflicts, which
  are version math the agent can't fix. Runs a bounded attempt loop (max attempts, token budget,
  per-attempt timeout) over one accumulating working tree. Every run that invokes the agent
  stores a full transcript. Exhausting the loop's bounds is guaranteed no worse than never having
  tried.
- **The agent is a real tool-using agent** (Microsoft Agent Framework), not a single prompt. Its
  tools are read-only exploration of the run's workspace — list files, read a file (or range),
  search text — plus **propose edit**. It can investigate beyond the files the diagnostics name
  (call sites, interfaces, extension methods, the package's new API surface where available).
- **It still never runs commands.** Restore/build are executed by the harness between attempts,
  not by the agent. Proposed edits remain constrained to existing, non-manifest, in-workspace
  source files; all tools are rooted in the workspace.
- **Status:** *partial.* The loop, bounds, edit guard, and transcripts are built. The agent itself
  (`MafRemediationAgent`) is a single chat prompt with the diagnosed files inlined (60k-char cap)
  and no tools; the `Microsoft.Agents.AI` packages are referenced but unused.

### 6.9 Change review gate
- An AI-repaired change that verifies is **held** (`AwaitingReview`) instead of pushed
  automatically; a mechanical (non-AI) bump is unaffected and pushes immediately as before. The
  stored proposal carries everything a review needs (before/after content, diagnostics, attempt
  count, base commit, verification time) with no live Azure DevOps call at review time. Four
  commands: **Approve** (push as-is, no re-verify), **Rebuild** (re-verify at current head),
  **Retry** (re-run the AI loop from the original diagnostics, dropping prior agent edits, fresh
  token budget), **Discard** (terminal, touches nothing). A stale approval (branch moved) is
  refused and reported as recoverable, never as a failure. A held repository gets no new scheduled
  runs and is visibly flagged (`SkippedHeld`, an overview panel). Review commands execute only in
  the worker, never in the API — the API only enqueues.

### 6.10 Pull request authoring
- Pushes to a deterministic per-repo branch (`autoremediator/dependency-updates`), reuses a single
  active PR rather than duplicating, and states in the PR description: the package summary,
  whether verification passed or was skipped (and why), AI-authorship + attempt count + transcript
  link when applicable, and (for an approved held proposal) the verification date/commit as a
  historical fact. No PR is opened when the update set is empty.

### 6.11 Run orchestration
- The state machine (§5) persists every run to Table Storage with a stable idempotency guarantee
  (one message → one run → the existing branch/PR is reused, never duplicated), and skips
  enqueueing a repository that's currently held for review.
- **Due-based scheduling.** Each repository has a schedule: a 5-field cron expression in UTC
  (seconds are rejected; no more often than every 15 minutes), from the repository's own settings,
  else the global default schedule, else the built-in `0 0 * * *` (daily at 00:00 UTC). The
  scheduler is a run-once tick (every 15 minutes when deployed) that enqueues only repositories
  that are **due**: never scheduled, or the next occurrence after the last scheduled time has
  passed. At most one run per repository per tick, whatever number of occurrences were missed (no
  backfill). A never-scheduled repository is due at the next tick. `SkippedHeld` is recorded
  only for a held repository that is due. Each repository's last scheduled time lives in the
  `schedulestate` table and is claimed with an ETag, so overlapping ticks cannot both enqueue it.
  This replaces the earlier pass that enqueued every enabled repository every 6 hours, so the
  default cadence is now daily.

### 6.12 Ad-hoc (manual) runs
- An operator can start a run for a single repository on demand ("Run now" on the repository in
  the UI, and an API endpoint), outside the schedule. It enqueues the same
  `RemediationRunRequested` message the scheduler does and follows the identical pipeline,
  idempotency, and review-gate rules — including refusing to start while the repository is held
  for review (409, with the held run id). The run records that it was manually triggered
  (trigger `Manual`, shown as a badge; scheduled runs are `Scheduled`).
- Run now works on a **disabled** repository, to try a repository before enabling it: disabled
  means "not scheduled", not "off". It does not change the repository's schedule state.
- The acting user is recorded on the run once §6.17 lands; it is not recorded today.
- **Status:** *built.* `POST /api/repositories/{id}/run` (202 `{ runId, enabled }`, 404, 409) and
  a Run now button on the Configuration page and the repository page.

### 6.13 Run observability UI
- API + Web pages for a runs feed (filterable by status) and a run detail view: updates table,
  PR link, verification outcome with diagnostics and a log link, and — when applicable — AI
  attempt count and transcript link. A verification rejection is visually distinct from an
  unexpected tool failure.
- **In-app attention signals** (the only notification channel for now): a persistent indicator in
  the app shell counting runs that need a person — `AwaitingReview` and unexpected `Failed` — with
  a list linking to each run. Held repositories show how long they've been held, so a stale hold
  is visible rather than silent. No email/Teams/Slack delivery.
- **Status:** feed, detail, and a "Held for review" panel on Home exist; the app-shell
  indicator and held-duration are *not built*.

### 6.14 Web UI foundation
- A **React + TypeScript single-page app** replaces the Blazor Server app, talking only to the
  existing HTTP API (the API stays the single backend; the SPA holds no server-side logic).
  Built with Vite; components from **shadcn/ui + Tailwind** (tables via TanStack Table).
  Keeps today's product shape: a dark-default GitHub-esque app shell with a light toggle and
  consistent styling across Home / Configuration / Dependency Map / Runs / Run detail.
- Composed by the Aspire AppHost for local development like every other service; deployed as
  static assets from its own `web` Container App (nginx), which also reverse-proxies `/api` to
  the API so the browser sees a single origin (no CORS) — see §6.18.
- **Status:** *built.* `AutoRemediator.Web` is the React SPA; the Blazor Server + MudBlazor app
  it replaced has been removed, and the Playwright functional suite targets the SPA's
  `data-testid`s. Authentication (§6.17) is not wired in yet; the API client has a seam for a
  bearer token.

### 6.15 Platform: service orchestration
- A .NET Aspire AppHost composes every service; Storage/Service Bus run as local emulators; a
  local model resource lets the AI loop be exercised with zero cloud dependency. The remediation
  worker treats the model as a **soft** dependency — an unavailable model costs only repair
  capability, never the mechanical pipeline. Local process shape (always-running) is documented
  as distinct from the deployed shape (scheduler = run-once cron ACA Job ticking every 15
  minutes, Terraform variable `scheduler_tick_cron_expression`; remediation = KEDA queue-scaled
  ACA Job).

### 6.16 Platform: solution scaffold & testing
- Inward-flowing project references (`Domain`/`Contracts` depend on nothing but `Shared`; no
  library depends on a host); vertical-slice API organization; MAF isolated in `Agents` with its
  contract owned by `Domain` so `Infrastructure` and `Agents` never reference each other. xUnit v3
  across projects + a Playwright functional suite; every scaffolded project carries at least one
  test.

### 6.17 Platform: security & identity
- Azure Storage/Service Bus clients resolve dual-mode from `ConnectionStrings:*` — an
  endpoint+`DefaultAzureCredential` (pinned to a user-assigned identity via `AZURE_CLIENT_ID`) in
  Azure, a connection string against local emulators otherwise — with a health check per resource
  and no explicit credential forced into the Aspire integrations (which would break the local
  emulator path).
- **User authentication: Microsoft Entra ID.** The SPA signs users in with MSAL and calls the API
  with a bearer token; the **API** validates the token and enforces authorization (the UI only
  hides controls — it is never the enforcement point). Two app roles:
  - **Admin** — manage repositories, targeting settings, and policy (plus everything Reviewer can).
  - **Reviewer** — review commands (Approve / Rebuild / Retry / Discard) and ad-hoc "Run now".
  - Any other signed-in user in the tenant is **read-only** (dashboards, runs, logs, transcripts).
- **Audit:** every review command and manual trigger records the acting user on the run; an
  approved PR's description names the approver.
- Local development runs with auth disabled by explicit configuration; a deployed environment
  refuses to start with auth disabled.
- **Status:** *not built.* No authentication exists today.

### 6.18 Infrastructure
- Modular Terraform (`modules/` reused by `environments/<env>/`), pinned versions, local state
  documented with a stated remote-backend migration path. The **test** environment: Consumption
  (scale-to-zero) Container Apps Environment, `api`/`web` as ingress Container Apps (`web` is an
  nginx image serving the React build and proxying `/api`), `scheduler`
  (cron) and `remediation` (KEDA on `remediation-runs` queue depth) as Container Apps Jobs, budget
  SKUs throughout, a single user-assigned managed identity with RBAC (no stored account
  keys/connection strings), Key Vault for the one credential RBAC can't replace (the Azure DevOps
  PAT), and images that build via a per-service Dockerfile from a repo-root context, named
  `<acr>/autoremediator/<service>:<short-sha|latest>`.

## 7. Key terminology

| Term | Meaning |
| --- | --- |
| Matched package | A package whose id matches a configured pattern (and no exclude) |
| Primary bump | A matched package raised to its policy target |
| Collateral bump | A declared sibling raised only to satisfy an intra-family dependency range |
| Policy target | The highest version allowed by the configured strategy relative to the current pin — **not** the absolute latest |
| Verified / DependencyFailure / Skipped | The three possible local-verification outcomes (§6.7) |
| AwaitingReview | Terminal-*for now* status holding an AI-repaired, verified change for a human decision |
| SkippedHeld | A scheduler bookkeeping run recording that a repository was not enqueued because it's currently held |
| Effective settings | The settings that actually apply to a repository: the global baseline layered with the repository's own settings, with the source of every value (§6.1) |
| Due (schedule) | A repository is due when it has never been scheduled, or the next occurrence of its cron schedule after its last scheduled time is at or before now (§6.11) |

## 8. Decisions & open questions

Questions the original specs were silent on. Resolved items record the decision and point to
the requirement it became; open items are deliberately deferred.

- [x] **No authentication or authorization.** *Resolved:* Entra ID with Admin/Reviewer app
  roles, enforced by the API — see §6.17.
- [x] **CI for this repository.** *Resolved:* GitHub Actions on every pull request — .NET
  build + test, and lint + typecheck + build for the React app. Azurite-dependent tests run against
  an Azurite service container (or keep self-skipping).
- [ ] **Deployment of this repository.** *Deferred — where/how is not decided yet.* How (and
  whether) images are published and Terraform is applied is left open; CI does not deploy.
- [ ] **No production/staging environment.** *Deferred with deployment.* Only the budget `test`
  Terraform environment exists, with local Terraform state; a production topology and a remote
  state backend are decided when IaC work resumes.
- [x] **No alerting/notification.** *Resolved:* in-app only for now — see §6.13. External
  channels (Teams/email/Slack) are a future direction.
- [x] **No data retention/cleanup policy.** *Resolved:* time-based retention configured by an app
  setting (e.g. `Retention:RunDays`). A periodic cleanup deletes runs older than the window along
  with their blobs (logs, transcripts, proposals); runs that are `AwaitingReview`, and each
  repository's most recent run, are never deleted. **Status:** *not built.*
- [x] **No stated scale/concurrency limits.** *Resolved:* design target is **tens of
  repositories**. Runs process sequentially per worker; Azure DevOps throttling is handled by
  honoring `429`/`Retry-After` rather than by a scheduler-side rate budget. Revisit when a real
  deployment's repo count is known. **Status:** covered by Aspire's standard resilience handler
  (applied to every `HttpClient`, including Azure DevOps), which retries `429` and honors
  `Retry-After`. **Risks to check:** that handler also retries non-idempotent `POST`s (a push
  that succeeded but timed out would be retried and then rejected as stale), and its default
  total-request timeout (30 s) may cut off large repository zip downloads.
- [x] **Single package ecosystem.** *Resolved:* NuGet only. Other ecosystems (npm, …) are a
  future direction; no ecosystem abstraction is built until a second one is real.
- [x] **No rollback story for an approved change.** *Resolved — non-goal:* the pull request is
  the unit of change; undoing a merged change is the target repository's own revert process.
- [x] **PAT rotation/expiry.** *Resolved:* rotation stays a manual secret update (Key Vault /
  user-secrets), but an expired or invalid PAT is **detected and surfaced**: Azure DevOps auth
  failures (`401`, or the `203` sign-in page ADO returns for bad credentials) end a run as a
  distinct *credential* failure rather than a generic `Failed`, raise an in-app attention signal,
  and stop the scheduler from fanning out runs that would all fail the same way.
  **Status:** *not built.*
- [x] **Multiple configured feeds — precedence unspecified.** *Resolved:* **mapped and loud.**
  Each feed declares the package patterns it serves (NuGet package-source-mapping style), so a
  package resolves only from its mapped feed(s); and each feed declares whether it receives the
  Azure DevOps PAT — by default only Azure DevOps-hosted feeds do. A feed failure is surfaced on
  the dependency map and the run as a feed error, never collapsed into `Unknown`.
  **Status:** *not built.* Today versions are unioned across all feeds, a failing feed is
  silently skipped, and the PAT is attached to **every** feed regardless of host
  (`FeedVersionResolver`, `PackageDependencyReader`) — a credential-exposure risk once
  non-ADO feeds are configured.
- [x] **AI cost ceiling beyond per-run.** *Resolved:* a global **daily token cap** app setting
  alongside the per-run `Agents:TokenBudget`. When exhausted, runs continue mechanically and record
  "AI budget exhausted" — the same soft-dependency path as an unavailable model. Token usage per
  run and per day is visible in-app. **Status:** *not built.*
- [x] **Per-repository settings and schedules.** *Resolved (feature 004):*
  - **Layering.** Global settings are the baseline; a repository may add lists and override
    scalars (§6.1). One "do not inherit" switch covers all targeting fields; the **schedule is
    excluded** (it is operational, so an unset repository schedule still uses the global default).
    With the switch off and no patterns, nothing matches and the effective view warns.
  - **Cron.** Schedules are 5-field cron expressions parsed with Cronos, in UTC only (no
    per-repository time zones). A global default schedule exists; the built-in default, when
    neither is set, is `0 0 * * *` (daily at 00:00 UTC).
  - **Cadence change.** The old scheduler enqueued every enabled repository on every fire, every
    6 hours. The scheduler is now a 15-minute tick and a repository with no schedule of its own
    runs once a day at 00:00 UTC. Operators who want the old cadence set the global default
    schedule to `0 */6 * * *`.
  - **Minimum interval (assumed, open to revisiting).** Schedules that can fire more often than
    every 15 minutes are rejected (checked over the next 100 occurrences). This was the planner's
    recommendation and was kept without an explicit answer; the alternative is to accept such
    schedules and document that they behave as "every tick".
  - **Anchor rule and never-scheduled.** The last scheduled time is set to the tick time (claimed
    with an ETag in `schedulestate`), so there is at most one run per repository per tick and no
    backfill after downtime. A never-scheduled repository is due at the next tick (a new
    repository, and every repository right after this feature is deployed, runs at the first
    tick).
  - **Terraform.** `scheduler_cron_expression` is renamed `scheduler_tick_cron_expression`
    (default `*/15 * * * *`) because its meaning changed from cadence to tick; a stale tfvars entry
    surfaces as an undeclared-variable warning instead of silently becoming a slow tick.
  - **Run now rules.** Refused (409) while the repository is held for review; allowed on disabled
    repositories ("disabled" means not scheduled); it does not touch schedule state (§6.12).
    Runs carry a trigger (`Scheduled` or `Manual`).
  - **API contract tightening.** `PUT /api/settings` now returns 400 with field-keyed errors for
    an unknown strategy or an invalid default schedule, where it used to coerce an unknown
    strategy to Minor.
  - **Organization routing is out of scope.** The per-repository `Organization` field stays
    editable but informational: every repository still goes through the single
    `AzureDevOps:OrganizationUrl` and one PAT (see the known defects).

### Known defects (found in the 2026-10-13 code review)

- **Base mismatch in planning:** manifests are read from the target branch, but edits are applied
  over the `autoremediator/dependency-updates` head when that branch exists
  (`RemediationRunner.cs:71`). Planning and applying must use the same base.
- ~~**Broken log/transcript links:**~~ *Fixed with the React SPA (§6.14).* `RunDetail.razor` built
  browser links from the internal `https+http://api` service-discovery address; the SPA uses the
  API's relative `/api` links as-is, served from the same origin.
- **"Open PRs" KPI is wrong:** counts runs that have a PR URL — not open PRs, and repeat runs on
  one PR are counted repeatedly. Conflicts with the "run ends at the PR" non-goal; replace with
  a metric the system actually knows (e.g. "runs that opened/updated a PR, last 7 days").
- **Swallowed errors:** Service Bus `ProcessErrorAsync` discards errors silently; feed failures
  collapse into `Unknown` (see feeds decision above).
- **Configuration UI gaps:** ~~repositories can't be edited or enabled/disabled in the UI (the API
  supports it)~~ *Fixed by feature 004 (§6.1, §6.12).* Still open: per-repository `Organization`
  is stored but ignored — one `AzureDevOps:OrganizationUrl` (and PAT) is used for everything. The
  UI labels the field informational. Routing needs a credential and a client per organization, so
  it is a feature of its own.
- **Dependency map is computed live** against Azure DevOps and every feed for every repository on
  each page load; it should come from the latest run's analysis (or a cached snapshot). It now
  uses each repository's effective settings, but is still live.
- **Flaky API test suite:** `AutoRemediator.Api.Tests` intermittently times out building the
  host (`WebApplicationFactory`, 5-minute host-factory timeout) — 3–5 of 30 tests fail on `main`.
- **Missing branch / unknown repository ends as `NoUpdates`:** when the target branch or the
  repository does not exist, the Azure DevOps client reports no manifests and the run finishes
  `NoUpdates`, indistinguishable from a healthy repo with nothing to bump. A misconfigured
  enrollment should surface as a failed run with a clear reason. Local repository mode
  (feature 002) deliberately copies this behaviour until it is fixed.

Found in the local-mode walkthroughs and verification (features 002, 003, 004); details in
[`docs/local-testing.md`](local-testing.md#findings):

- **Empty commit on re-run:** a re-run for a repository that already has the update branch pushes a
  new commit with an identical tree, so the branch grows by one empty commit per run. Run now makes
  re-runs routine.
- **"AI remediation" wording for a no-model run:** with no model configured (0 attempts), the run
  detail still says a compile break "was handed to an AI agent, which proposed source edits".
- **Approve clears the run's Updates table:** after Approve a run is `Completed` with an empty
  `updates` list and no verification object, so the Updates table and the verification section
  disappear.
- **Stale PR diff links:** `pullRequestDiffUrl` is set for every `Completed` local run, including
  runs whose PR record no longer exists; the page then shows a terse "404 Not Found".
- **`NU1605` triplicate diagnostics:** one restore error (`NU1605` on `restore-conflict`) is shown
  as three diagnostics because the dependency-chain continuation lines are parsed as separate
  diagnostics.
- **Dependents do not recover after a failed local-scenarios provisioning:** after `local-scenarios`
  fails and later succeeds, `api`, `remediation` and `scheduler` stay `FailedToStart` and must be
  started by hand.
- **`-Reset`/`-Force` during a scheduler pass corrupts it:** the generator lock does not guard the
  running app, so runs in flight end with missing branches or PR records, or against replaced repos.
- **Transitive vulnerability advisory in Infrastructure:** `System.Security.Cryptography.Pkcs`
  8.0.1 arrives transitively and is flagged (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7).
- **Azurite-backed tests that hardcode the default port skip under Aspire:** the tests use
  `UseDevelopmentStorage=true`, but Aspire runs Azurite on another port, so they self-skip.
- **Intermittent test failure:** `VerificationEndToEndTests.No_workspace_directory_survives_a_run`
  failed intermittently in the full solution run (not in isolation).
- **Token-usage test fails against Ollama:** the local-model agent test that requires reported token
  usage failed against Ollama, so the token budget may not be enforced with that provider.
- **Terraform not validated:** `terraform validate` was not run for the feature 004 changes (the
  variable rename).
- **Deployed model setup is still Foundry:** the Terraform/deployed shape still provisions a Foundry
  model; local mode uses Ollama.

## 9. Future directions

Decided as *not now*, recorded so the architecture doesn't preclude them:

- **GitHub support** — second source-control platform alongside Azure DevOps.
- **Package-publish trigger** — publishing `Contoso.Core 2.0` fans out runs to every consuming
  repository, instead of waiting for the next scheduled run.
- **Additional package ecosystems** — e.g. npm, behind an ecosystem seam introduced when needed.
- **External notifications** — Teams/email/Slack delivery of the in-app attention signals.
- **Post-PR tracking** — follow the opened PR through CI and merge; feeds real outcome metrics.

## 10. References

- Repo layout and local run instructions: [`README.md`](../README.md)
