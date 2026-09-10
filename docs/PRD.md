# AutoRemediator — Product Requirements Document

> **Provenance:** synthesized from the capability specs in [`docs/specs/`](specs/) and
> [`ROADMAP.md`](../ROADMAP.md), which are the durable, present-tense record of what is
> actually built (per the project constitution). This document reorganizes that material
> into PRD form and calls out where a spec is silent so gaps can be filled deliberately
> rather than discovered in production. Where this document and a capability spec
> disagree on behavior, **the spec wins** — file an issue and reconcile them; this PRD is
> a synthesis, not a new source of truth.

## 1. Problem statement

Teams that consume a shared internal package family (e.g. `Contoso.*`) across many Azure
DevOps repositories fall behind on updates because bumping a dependency is low-value,
easy-to-defer work — until a breaking change accumulates into a large, risky jump. AutoRemediator
keeps repositories continuously current against that package family: it finds outdated matched
packages, bumps them, verifies the result builds, attempts an AI-assisted repair when a bump
breaks compilation, and opens a pull request — holding any AI-authored change for a human decision
before it ever reaches the repository.

## 2. Goals

- Keep a configured, explicit set of Azure DevOps repositories current against a configured
  package family, on a schedule, with no manual triage for the mechanical case.
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

## 3. Non-goals (decided, per ROADMAP.md and the specs)

- **No organization crawl / repository discovery.** Repositories are an explicit allowlist;
  AutoRemediator never discovers new repos on its own.
- **No general-purpose dependency bot.** Targeting is package-pattern-first (e.g. `Contoso.*`),
  not "update everything."
- **Not a substitute for the target repository's own CI.** Local verification is a fast,
  approximate signal (restore + build only — no tests, analyzers, or custom build targets); the
  repository's own Azure DevOps CI remains the authority on the resulting pull request.
- **The agent never runs commands and never edits version-selection files.** It proposes source
  edits only, inside the existing working tree; manifests, lock files, and verification artifacts
  are off-limits.
- **Nothing is ever pushed outside a pull request**, and never to a protected/target branch
  directly, whether or not an agent contributed.

## 4. Users

- **Operator** — the person who configures targeted repositories/patterns/policy, watches the
  Runs feed, and reviews/approves AI-repaired changes. Today this is any person who can reach the
  Web app (see [§8, Gap: authentication](#8-known-gaps--open-questions)).
- **Downstream reviewer** — a repository maintainer who reviews the pull request AutoRemediator
  opens, informed by its verification/AI-authorship disclosure.

## 5. System overview

```
  scheduler (cron) ─▶ for each enabled, non-held repo ─▶ enqueue RemediationRunRequested
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

Each subsection is a capability with its own detailed spec (requirements + Gherkin scenarios) in
`docs/specs/`; this section is a condensed index of *what* each one guarantees, not a
restatement of the full requirement text.

### 6.1 Configuration & targeting
*Spec: [target-configuration](specs/target-configuration.md)*
- Persists an explicit, enable/disable-able list of managed repositories (org/project/name +
  target branch) and global targeting settings (package patterns, excludes, feeds, policy) in
  Table Storage, editable via API and a Blazor page.

### 6.2 Azure DevOps connectivity
*Spec: [azure-devops-connectivity](specs/azure-devops-connectivity.md)*
- One authenticated client (PAT from Key Vault in Azure, user-secrets locally) handles reads
  (manifest discovery/content, repo verification, full-tree-as-zip download) and writes (push to
  branch, create-or-find PR) entirely over REST — no `git` clone anywhere in the system.

### 6.3 Dependency analysis
*Spec: [dependency-analysis](specs/dependency-analysis.md)*
- Parses `Directory.Packages.props` and `*.csproj`, matches package ids against configured
  include/exclude globs, resolves each match's status (`Outdated` / `UpToDate` / `Ignored` /
  `Unknown`) against the **policy target** (not the absolute latest), and exposes the result as a
  read-only dependency map in the UI.

### 6.4 Update policy
*Spec: [update-policy](specs/update-policy.md)*
- Per-scope strategy (`Patch`/`Minor`/`Major`) selects the highest available version within that
  band relative to the current pin; an ignore-glob list holds matched packages; pre-release
  versions are excluded unless explicitly allowed.

### 6.5 Dependency graph alignment (collateral bumps)
*Spec: [dependency-graph-alignment](specs/dependency-graph-alignment.md)*
- Reads intra-family dependency ranges from feed metadata (no clone/build) and raises declared
  sibling packages just enough to satisfy those ranges, escalating beyond policy only when
  correctness requires it, iterating to a fixpoint. Only declared packages are touched; undeclared
  transitive dependencies are left to NuGet. Collateral bumps and policy escalations are surfaced
  distinctly in the update set and the PR summary.

### 6.6 Update execution
*Spec: [update-execution](specs/update-execution.md)*
- Computes the update set (primary + collateral), edits manifest text in place (and carries a
  regenerated `packages.lock.json` when the repo commits one), and defers to local verification
  before anything is pushed. The repository's own CI remains the correctness authority; local
  verification never runs tests.

### 6.7 Local verification
*Spec: [local-verification](specs/local-verification.md)*
- Downloads the repo tree as a zip (no clone), applies the computed edits, resolves the build
  target (shallowest solution, else every project), runs gated `restore` → `build` with timeouts,
  and classifies the outcome as **Verified** / **DependencyFailure** / **Skipped** — defaulting to
  `Skipped` whenever a failure can't be positively attributed to the dependency change, so an
  environment problem is never mistaken for a real break. Diagnostics and full logs are captured
  as run artifacts; the temp workspace is always deleted, on every exit path.

### 6.8 AI remediation loop
*Spec: [ai-remediation-loop](specs/ai-remediation-loop.md)*
- Invoked only on `CS`-coded (compile) diagnostics — never on restore-time (`NU`) conflicts, which
  are version math the agent can't fix. Runs a bounded attempt loop (max attempts, token budget,
  per-attempt timeout) over one accumulating working tree; proposed edits are constrained to
  existing, non-manifest, in-workspace source files; the agent can propose edits only, never run
  commands. Every run that invokes the agent stores a full transcript. Exhausting the loop's
  bounds is guaranteed no worse than never having tried.

### 6.9 Change review gate
*Spec: [change-review-gate](specs/change-review-gate.md)*
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
*Spec: [pull-request-authoring](specs/pull-request-authoring.md)*
- Pushes to a deterministic per-repo branch (`autoremediator/dependency-updates`), reuses a single
  active PR rather than duplicating, and states in the PR description: the package summary,
  whether verification passed or was skipped (and why), AI-authorship + attempt count + transcript
  link when applicable, and (for an approved held proposal) the verification date/commit as a
  historical fact. No PR is opened when the update set is empty.

### 6.11 Run orchestration
*Spec: [run-orchestration](specs/run-orchestration.md)*
- The state machine (§5) persists every run to Table Storage with a stable idempotency guarantee
  (one message → one run → the existing branch/PR is reused, never duplicated), and skips
  enqueueing a repository that's currently held for review.

### 6.12 Run observability UI
*Spec: [run-observability-ui](specs/run-observability-ui.md)*
- API + Web pages for a runs feed (filterable by status) and a run detail view: updates table,
  PR link, verification outcome with diagnostics and a log link, and — when applicable — AI
  attempt count and transcript link. A verification rejection is visually distinct from an
  unexpected tool failure.

### 6.13 Web UI foundation
*Spec: [web-ui-foundation](specs/web-ui-foundation.md)*
- MudBlazor design system, a dark-default GitHub-esque app shell with a light toggle, consistent
  styling across Home / Configuration / Dependency Map / Runs.

### 6.14 Platform: service orchestration
*Spec: [service-orchestration](specs/service-orchestration.md)*
- A .NET Aspire AppHost composes every service; Storage/Service Bus run as local emulators; a
  local model resource lets the AI loop be exercised with zero cloud dependency. The remediation
  worker treats the model as a **soft** dependency — an unavailable model costs only repair
  capability, never the mechanical pipeline. Local process shape (always-running) is documented
  as distinct from the deployed shape (scheduler = run-once cron ACA Job; remediation = KEDA
  queue-scaled ACA Job).

### 6.15 Platform: solution scaffold & testing
*Specs: [solution-scaffold](specs/solution-scaffold.md), [testing-foundation](specs/testing-foundation.md)*
- Inward-flowing project references (`Domain`/`Contracts` depend on nothing but `Shared`; no
  library depends on a host); vertical-slice API organization; MAF isolated in `Agents` with its
  contract owned by `Domain` so `Infrastructure` and `Agents` never reference each other. xUnit v3
  across projects + a Playwright functional suite; every scaffolded project carries at least one
  test.

### 6.16 Platform: security & identity
*Spec: [managed-identity-auth](specs/managed-identity-auth.md)*
- Azure Storage/Service Bus clients resolve dual-mode from `ConnectionStrings:*` — an
  endpoint+`DefaultAzureCredential` (pinned to a user-assigned identity via `AZURE_CLIENT_ID`) in
  Azure, a connection string against local emulators otherwise — with a health check per resource
  and no explicit credential forced into the Aspire integrations (which would break the local
  emulator path).

### 6.17 Infrastructure
*Specs: [terraform-iac](specs/terraform-iac.md), [azure-test-environment](specs/azure-test-environment.md), [container-images](specs/container-images.md)*
- Modular Terraform (`modules/` reused by `environments/<env>/`), pinned versions, local state
  documented with a stated remote-backend migration path. The **test** environment: Consumption
  (scale-to-zero) Container Apps Environment, `api`/`web` as ingress Container Apps, `scheduler`
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

## 8. Known gaps / open questions

These are areas the specs are **silent** on — either genuinely undecided, explicitly flagged as
a caveat in a spec, or out of scope for the slice that shipped. Listed so they can be resolved
deliberately rather than assumed.

- [ ] **No authentication or authorization.** `change-review-gate` explicitly flags this: anyone
  who can reach the Web app can approve a change that pushes to the target repository. Accepted
  for local use; **must be resolved before any deployed (non-local) exposure.** Who should be
  allowed to approve? Is there a role distinction between "can configure repos/policy" and "can
  approve a push"?
- [ ] **CI/CD for this repository itself.** The Azure Pipelines setup that built/deployed this
  repo was just removed as part of the GitHub migration; nothing replaces it yet (see recent
  chore commits). Needs a decision: GitHub Actions, keep Azure Pipelines against the GitHub repo,
  or something else — and whether it deploys to the existing Terraform `test` environment or a
  new one.
- [ ] **No production/staging environment.** `terraform-iac`/`azure-test-environment` define only
  a budget-oriented `test` environment. Is there a target production topology (higher SKUs, HA,
  a real backend for Terraform state, alerting)?
- [ ] **No alerting/notification.** Health checks exist, but nothing pages or notifies anyone when
  a repository is held for review, a run fails unexpectedly, or the model becomes unavailable for
  an extended period. Should `AwaitingReview`/`Failed` trigger a notification (email, Teams,
  Slack)?
- [ ] **No data retention/cleanup policy.** Run history, verification logs, and AI transcripts
  accumulate in Table/Blob Storage indefinitely per the specs as written. Is there a retention
  window, or an expectation this grows unbounded?
- [ ] **No stated scale/concurrency limits.** Nothing specifies how many repositories can be
  configured, how many runs process concurrently, or how the system avoids hitting Azure DevOps
  REST rate limits as the repo count grows.
- [ ] **Single package ecosystem.** Everything is scoped to NuGet (`Directory.Packages.props`,
  `*.csproj`). Is there any future intent to cover npm, or is that explicitly out of scope
  long-term (not just for the shipped slices)?
- [ ] **No rollback story for an approved change.** Once a proposal is approved and the PR is
  merged downstream, there's no AutoRemediator-side "undo" — is that intentionally the target
  repo's own revert process, or does this need a feature?
- [ ] **PAT rotation/expiry.** The Azure DevOps PAT lives in Key Vault, but nothing specifies an
  expiry/rotation process or what happens to in-flight runs when it expires.
- [ ] **Multiple configured feeds — precedence unspecified.** `target-configuration` allows "one
  or more" feeds; `dependency-analysis`/`local-verification` don't specify resolution order or
  conflict handling when the same package could resolve from more than one.
- [ ] **AI cost ceiling beyond per-run.** `Agents:TokenBudget` bounds a single run, but there's no
  stated daily/monthly cost ceiling across all repositories — worth deciding before this scales
  past a handful of repos.
- [ ] **Deleted `.specify`/Spec Kit tooling still referenced by slash commands.** `.claude/skills`
  and `.specify` were just removed from the repo (each dev now brings their own), but the
  `speckit-*` skills that used to ship here read from `.specify/` at runtime. Decide whether
  Spec Kit is still the intended workflow for new capability specs going forward, and if so, how
  a new contributor sets it up.

## 9. References

- Capability specs: [`docs/specs/`](specs/) (source of truth for exact requirements/scenarios)
- Product roadmap and slice sequencing: [`ROADMAP.md`](../ROADMAP.md)
- Repo layout and local run instructions: [`README.md`](../README.md)
