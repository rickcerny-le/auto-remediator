# Decisions

Append-only.

## 2026-10-13 · 001-ui-redesign
- **Theme defaults to `system`** (modes light | dark | system, applied pre-paint, no flash). Reason: respects OS preference; user chose it.
- **Inter self-hosted** via `@fontsource-variable/inter`, no CDN. Reason: user approved a web font; self-hosting avoids an external dependency.
- **axe runs in the C# functional tests via `Deque.AxeCore.Playwright`**, not `@axe-core/playwright`. Reason: the test project is C#; same engine and rule tags. User approved.
- **DataTable rows navigate through a real link in the primary cell**; row click is a mouse convenience only. `caption` is a required prop so every table has an accessible name.
- **Desktop sidebar and mobile drawer are separate renders** (matchMedia hook), keeping one nav landmark and a unique `data-testid="nav"`.
- **Light `--success` split from `--primary`** (#166c2e text/pills vs #1a7f37 button) to meet 4.5:1.
- **Dark outline buttons are transparent** (was `bg-input/30`) after axe found a Discard contrast failure; affects every outline button in dark mode.
- **Table scroll regions are always focusable** (`role="region"`, `tabIndex=0`); conditional focus would need overflow detection.
- **Scope kept small**; charts, logo, density toggle, i18n, extra animation deferred.

## 2026-10-13 · 002-local-repo-mode
- **Local PR = idempotent JSON record under `.local/prs` with a `file://` URL**; push = temporary worktree plus compare-and-swap ref update. The git binary is allowed only in local mode (the Azure DevOps path stays git-free).
- **Local mode is selected by `AzureDevOps:Mode=Local`** in `AddInfrastructure`; the default registration is unchanged.
- **Scenario generator is a committed PowerShell 7 script under `tools/local-scenarios`**, writing to the gitignored `.local/`, absolute feed paths computed at generation time, NuGet cache purged for its own ids.
- **Scenarios run under global settings profiles** (baseline / patch / major / prerelease); per-repo settings are out of scope.
- **Missing branch / unknown repo ending as `NoUpdates` is logged as a known defect in the PRD** (user decision). Local mode copies the behaviour until it is fixed.
- **Ollama is the model provider going forward** (user decision), replacing Foundry. Prior art: `feat/local-remediation` (commit 5071d9f), copied from rather than merged.
- **With `Model:Enabled=false` remediation is non-agentic**: the loop falls back to `UnavailableRemediationAgent`, and a build that fails after the bump ends the run as `VerificationFailed` with no PR (existing behaviour, user-confirmed intent).
- **Held-review seeder runs as an explicit-start Aspire resource, registered only when `LocalRepos:Enabled` is on** (user decision, 2026-10-13). Aspire injects the Azurite connection strings, so no manual copying. Replaces the env-var approach assumed in task 08.
- **PR diff limits accepted:** full before/after per file, 256 KB per side, 200 files, binaries listed not shown; PR record refreshes base/head on every push.
- **Deployed model setup (`infra/terraform` Foundry) is left untouched** and revisited in the next feature.
- **Every interactive element must use the shared `focusRing`** (`outline-hidden` + `focus-visible:outline-solid ...`). `outline-none` stays only on programmatic-focus targets (`main`, page `h1`). Reason: in Tailwind v4 `outline-none` sets `--tw-outline-style: none` which `outline-2` reads, so no ring rendered anywhere after feature 001; axe cannot detect it, so `FocusVisibleTests` checks computed styles in a real browser.
- **Held-review seeder** is `tools/local-scenarios/HeldRunSeeder`, resource `held-run-seeder` (explicit start, local mode only), as decided.
- **PR diff** exposed as `pullRequestDiffUrl` + `GET /api/runs/{id}/pull-request-diff`, local mode only; `PullRequestChangeKind` enum in Infrastructure, `ChangeKind` string in the DTO.
- **No Aspire version bump** for Ollama; only `Microsoft.Extensions.Options.ConfigurationExtensions` raised to 10.0.11 in Agents.
- **Open follow-up:** `The_real_model_call_reports_token_usage` fails against Ollama (no usage reported), so the token budget may not be enforced under Ollama.

## 2026-10-14 · 003-self-provisioning-scenarios
- **AppHost runs the generator through a small C# wrapper** (`tools/local-scenarios/LocalScenarioSetup`, modes provision/reset, later enroll), not pwsh directly. Exit codes: 0 ok, 1 script/argument failure, 2 missing tool, 3 generator lock held. Porting the generator to C# is a later feature.
- **Fingerprint** = hash of the three generator files (LF-normalised), normalised absolute root, `dotnet --version`, and a version constant; stored in `.local/scenarios.fingerprint.json`; deleted first and written last. "Intact" is a structure check, so hand commits on `main` survive.
- **Reset deletes only `autoremediator/*` branches** and empties `.local/prs`; leaves feed, fingerprint, `main` commits, `scenario/agent-fix`, NuGet cache, Azurite and anything outside `.local`. `-Reset` refuses on stale output and cannot combine with `-Force` or `-ResetIfCurrent`.
- **Generator passes `--disable-build-servers`** to dotnet (no measurable cost) so killed builds leave no locked folders.
- **Tools test project lives under the sln `tests` folder.**
- **Scheduler is opt-in in local mode** (user decision): `WithExplicitStart()` unless `LocalRepos__RunOnStart=true`, in which case it waits for enrollment. **Reset on warm start is on by default** (`LocalRepos__ResetOnStart`). Default enrolled profile is `baseline` (`LocalRepos__Profile`).
- **Enrollment** runs as `local-scenarios-enroll` (wrapper `enroll` mode): API URL from Aspire-injected `services__api__http__0` (no hard-coded ports), `/health` poll up to 120 s, profile validated before preflight so a bad value fails only that resource. `api` gets a `/health` check in local mode only.
- **Aspire 13.5.1 behaviour recorded:** a non-zero exit shows as `Finished <code>`; dependents go `FailedToStart` and do not recover on a later success; `web` stays `Waiting` after an `api` failure; `restart` needs a resource that has run before (use `start` for the first run).
- **Favicon:** replaced the leftover Blazor `favicon.png` with `favicon.svg` (emerald square, dark diamond).

## 2026-10-14 · 004-per-repo-settings
- **Layered settings:** one global baseline + optional per-repo settings. Lists (patterns, excludes, feeds, ignore) union with global (global first, case-insensitive dedupe); strategy/pre-release use the repo value when set, else global. One per-repo "do not inherit" switch covers all targeting fields but NOT the schedule. Presets deferred.
- **Cron scheduling:** Cronos, 5-field, UTC. Built-in default schedule is **`0 0 * * *` (daily)** (user decision); this deliberately changes deployed cadence from 6-hourly to daily (restore with global default schedule `0 */6 * * *`). The scheduler ticks every 15 min and enqueues only due repos; never-scheduled repos run at the next tick; at most one run per repo per tick.
- **Terraform:** `scheduler_cron_expression` is renamed `scheduler_tick_cron_expression` (default `*/15 * * * *`), no alias, because its meaning changes from cadence to tick.
- **Run now is allowed on disabled repos** (user decision) and refused (409) only while held; the 202 body is `{ runId, enabled }`; runs record a Scheduled/Manual trigger carried on the queue message.
- **Minimum cron interval 15 minutes is ASSUMED** (planner recommendation, not an explicit user answer); revisit if wanted.
- **Per-repo Organization routing is out of scope** (needs a credential and client per organization); the field is labelled informational.
- **Superseded:** 002's "scenarios run under global settings profiles" and 003's default profile `baseline` (replaced by per-repo settings in task 10).
- **Feature 004 verified:** real cold start enrolls per-repo settings automatically; due-based ticks behave (one run per due occurrence); Run now works on normal/disabled repos and is refused (409) while held.
- **Workspace test race (finding 19):** `No_workspace_directory_survives_a_run` compares the shared temp directory count; reproduce-by-churn proved it racy. Fix by counting only the test's own directories (not yet done).
