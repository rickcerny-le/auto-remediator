# Local repository mode and realistic upgrade scenarios

**Status:** done
**Created:** 2026-10-13

## Goal
Run the whole system on one machine against git repositories and a NuGet folder feed on disk, so the user can do integration and manual testing without Azure DevOps or a working model. A `LocalRepositoryClient` stands in for `IAzureDevOpsClient`, the model provider moves from Foundry to Ollama and becomes optional, the AppHost gains a local-repos switch, and a committed generator builds a catalogue of realistic upgrade scenarios (feed packages and demo repos) with one command. The run detail page shows a read-only diff of a local PR. Verification still runs for real. With the model disabled, remediation is non-agentic (see "Non-agentic mode" under Context).

## Scope
**In:**
- `LocalRepositoryClient : IAzureDevOpsClient` (git binary): manifests and archive from a local repo, push creates/updates the update branch, `EnsurePullRequestAsync` writes an idempotent JSON PR record (source/target branch, base and head commit ids) and returns a `file://` URL.
- Ollama replaces Foundry as the model provider (AppHost resource and Agents client), copied from `feat/local-remediation` commit `5071d9f` without merging; `Model:Enabled=false` leaves the model resource out.
- A read-only PR diff (local mode only): an API endpoint linked from the run DTO, rendered on the run detail page with the proposal panel's file accordion, meeting feature 001's WCAG 2.2 AA rules.
- Configuration switch in `AddInfrastructure` (`AzureDevOps:Mode=Local`, `AzureDevOps:LocalRoot`, `AzureDevOps:LocalPullRequestsRoot`); the Azure DevOps REST path stays the default and unchanged.
- Proof (tests) that local folder feeds work through `FeedVersionResolver`, `PackageDependencyReader` and the workspace `NuGet.config`, with and without a PAT configured.
- An automated local-mode end-to-end test through the real `RemediationRunner`.
- AppHost: `LocalRepos:Enabled` (default `false`) to wire local mode into `api`, `scheduler` and `remediation` with no PAT.
- `tools/local-scenarios/`: PowerShell 7 generator for the feed and demo repos, an enrollment/settings script with policy profiles, and a small seeder for the held-for-review scenario.
- `.local/` added to `.gitignore`.
- A manual walkthrough doc with an expected-results matrix, executed once with actual results recorded.

**Out:**
- Improving the AI agent or its repair quality; deployed model wiring and `infra/terraform` (which still provisions Azure AI Foundry).
- Anything else from `feat/local-remediation` beyond the pieces named in tasks 01, 04 and 05; the branch is not merged.
- A "Run now" endpoint (PRD §6.12); runs are triggered by restarting the `scheduler` resource.
- Per-repository targeting settings (settings stay global; profiles are applied one at a time).
- Fixing the PRD "Known defects" (base mismatch in planning, missing branch reads as no manifests). The walkthrough records where they show up.
- Any action on a local PR (approve, merge, close) and any change to the production run flow: a run still ends at the PR (PRD non-goal). The diff view is read-only, and there is none for Azure DevOps PRs.

## Acceptance criteria
- [ ] With `AzureDevOps:Mode` unset, DI resolves the existing `AzureDevOpsClient` exactly as before; with `Local`, it resolves `LocalRepositoryClient`, and no PAT is needed anywhere.
- [ ] `pwsh tools/local-scenarios/New-LocalScenarios.ps1` recreates `.local/feed`, `.local/repos` and `.local/prs` from nothing, with no hard-coded user path, and every demo repo builds at `main`.
- [ ] With no switches set, `aspire start` runs Ollama (`ollama` + `chat`) and the `remediation` worker gets `ConnectionStrings:chat` in the form `Endpoint=...;Model=phi4`; no Foundry package or reference remains in `src`, `tests` or the README.
- [ ] `LocalRepos__Enabled=true` and `Model__Enabled=false` set before `aspire start` give a running app with no model resource and no Azure DevOps dependency; without `LocalRepos__Enabled`, the Azure DevOps path is composed exactly as before.
- [ ] Non-agentic mode: with `Model__Enabled=false`, a bump that breaks the build ends `VerificationFailed` with 0 remediation attempts, a "No model is configured" transcript, no update branch and no PR; the UI shows it amber (automated in task 03, observed in task 10).
- [ ] After enrolling the scenarios and restarting `scheduler`, each scenario reaches the status in the walkthrough's expected-results matrix for the active profile (Completed with branch + PR record, NoUpdates, VerificationFailed with CS or NU diagnostics, Ignored package untouched, collateral bump flagged beyond policy, lock file updated).
- [ ] A `Completed` run shows a PR link in the UI; `git -C .local/repos/<repo> diff main..autoremediator/dependency-updates` shows the bump; re-running produces no second PR record.
- [ ] In local mode, a `Completed` run's detail page shows a read-only "Pull request changes" panel whose files and before/after content match `git diff base..head` for the PR record; it has no action buttons, passes axe (WCAG 2.2 AA) in both themes, and every existing `data-testid` still exists. In Azure DevOps mode the run DTO has no diff link.
- [ ] The held-for-review scenario can be seeded, appears on Home, the scheduler records `SkippedHeld` for it, and Approve / Rebuild / Retry / Discard behave as documented (Retry reports no model configured).
- [ ] `dotnet test AutoRemediator.sln` passes; the new git-dependent tests self-skip when `git` is not on PATH.

## Context
**Seams:**
- `src/AutoRemediator.Infrastructure/AzureDevOps/IAzureDevOpsClient.cs`: 6 operations. Paths are rooted with a leading `/` (ADO style); `PushRejectedException(typeKey)` signals compare-and-swap failures (`GitRefUpdateStaleException`, `GitItemNotFoundException`).
- `AzureDevOpsClient.cs`: reference semantics to mirror. On a missing repo or branch, `GetManifestsAsync` returns an empty list (the run then ends `NoUpdates`), and `GetBranchHeadAsync` returns null. `EnsurePullRequestAsync` returns the existing active PR unchanged if one exists.
- `InfrastructureExtensions.AddInfrastructure`: registers the client via `AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>`. Used by `api` (dependency map), `scheduler` and `remediation`.
- `Feeds/FeedVersionResolver.cs` and `Alignment/PackageDependencyReader.cs` attach the PAT to every feed via `PackageSource.Credentials`. With `Pat` empty, no credentials are attached.
- `Verification/VerificationWorkspace.cs` extracts the archive and strips a single top-level "wrapper" folder unless it is a known source folder name. It writes a root `NuGet.config` with the settings feeds first and the repo's own sources copied verbatim after them, so a relative path in a repo `NuGet.config` breaks inside the temp workspace. `DiagnosticParser` treats `NU1301` (source unreachable) as environmental, giving `Skipped`, and the change would then be pushed with "verification skipped".
- `Remediation/RemediationRunner.cs`: the AI loop is entered only for `CS` diagnostics. With no chat connection, `MafRemediationAgent.IsAvailable` is false and the loop records "No model is configured" with 0 attempts, ending in `VerificationFailed`. That is the intended local behaviour (see "Non-agentic mode" below).
- `Api/Features/Runs/RunsEndpoints.cs`: run DTO links (`ProposalUrl`, `TranscriptUrl`, `LogUrl`) are relative `/api/runs/{id}/...` paths, or null when there is nothing to fetch. The PR diff link (task 09) follows the same pattern. `Web/src/pages/run-detail.tsx` renders the proposal's files in an `Accordion` with Before/After `pre` blocks; task 09 extracts that into a shared component without changing its markup.
- `Agents/AgentsExtensions.cs` registers a chat client only when `ConnectionStrings:chat` is present. Task 04 swaps `AddAzureChatCompletionsClient` for `AddOllamaApiClient(...).AddChatClient()`. Test-only env var `AUTOREMEDIATOR_CHAT_CONNECTION` uses the format `Endpoint=http://localhost:11434;Model=<name>` (the real Ollama port comes from `aspire describe`).

**Non-agentic mode (`Model:Enabled=false`):** remediation is non-agentic. The AppHost creates no model resource and `remediation` gets no `ConnectionStrings:chat`, so no chat client is registered. The remediation agent reports itself unavailable: the worker's `MafRemediationAgent` has no client, and hosts that never call `AddAgents` (such as task 03's tests) get the `UnavailableRemediationAgent` fallback. A build that fails after the bump ends the run as `VerificationFailed` with 0 attempts, a "No model is configured" transcript, no branch and no PR. This is existing `RemediationRunner` behaviour; no code changes for it. The UI shows `VerificationFailed` in amber (warning tone, `lib/status.ts`), because the tool worked and the bump did not. Restore (`NU`) failures never enter the loop in any mode.

**Accessibility (feature 001):** UI changes keep WCAG 2.2 AA:
- the shared `focusRing` from `src/lib/utils.ts` on interactive elements
- a `caption` on every `DataTable`
- no colour-only signals
- every existing `data-testid` kept
- axe clean in both themes via `tests/AutoRemediator.Web.FunctionalTests/AccessibilityTests.cs`
- `Review/ReviewCommandHandler.cs`: Approve pushes the stored proposal and calls `EnsurePullRequestAsync`. Rebuild re-verifies at the current head. Retry is blocked when no agent is available. `AwaitingReview` is reachable only when the AI loop repaired something, so locally it must be seeded the way `tests/AutoRemediator.Web.FunctionalTests/ReviewGateTests.cs` does.
- `Worker.Scheduler/SchedulerWorker.cs` runs once at startup and enqueues every enabled repository, skipping held ones with a `SkippedHeld` run. Re-trigger it by restarting the resource, or with `Scheduler:DevLoopEnabled`.
- `Domain/TargetingSettings.cs`: one global settings record (patterns, excludes, feeds, strategy, ignore globs, allowPrerelease). That is why scenarios use profiles.
- `Api/Features/Repositories` and `Settings` have no validation (a feed can be a local path). Each `POST` creates a new id, so enrollment must list first and upsert by name.

**Prior art:** the unmerged branch `feat/local-remediation` already has `LocalGitClient` (`git show feat/local-remediation:src/AutoRemediator.Infrastructure/AzureDevOps/LocalGitClient.cs`), its tests, `scripts/new-local-fixture.ps1` (the source of the current `.local/` spike), a `Model:Enabled` AppHost switch, the Foundry-to-Ollama swap (`5071d9f`) and an unreachable-model soft-dependency fix (`c336726`). It predates the React UI, so **copy pieces from it with `git show`/`git diff`; do not merge, cherry-pick or rebase it.** The differences required here are listed in tasks 01, 04 and 05.

**Environment facts (checked):** git 2.54 and pwsh 7.6 are installed, along with .NET SDK 10.0.300. `~/.nuget/packages/contoso.core` already exists from the spike, which demonstrates the stale-cache risk below. `.gitignore` already ignores `*.nupkg`, `nuget.config` (case-insensitive on Windows) and `**/[Pp]ackages/*`, so never name a committed folder `packages` under `tools/`.

**Decisions made with the user (Benny to log):**
- Local PR behaviour: push creates or updates the update branch in the local git repo using the git binary. The PRD "no git" rule applies only to the Azure DevOps path. `EnsurePullRequest` writes JSON under `.local/prs` and returns a `file://` URL, idempotently.
- Scenarios come from a committed, deterministic PowerShell generator under `tools/local-scenarios`. It builds the nupkgs and git repos under the gitignored `.local/`, and computes the `NuGet.config` paths at generation time.
- The local client is selected by configuration in `AddInfrastructure`. Feeds are plain local folder paths through the existing NuGet.Protocol code.
- The model is optional in the AppHost via a flag (`Model:Enabled`).
- Ollama is the model provider going forward, replacing Foundry (logged).
- With `Model:Enabled=false`, remediation is non-agentic (logged; existing behaviour).
- The missing-branch / unknown-repo `NoUpdates` behaviour is a PRD Known defect (logged, already in `docs/PRD.md`); local mode copies it.
- Local mode shows a read-only PR diff in the UI from the PR record's base/head commits; no approve step.

**Planner choices (confirm or override):**
- Config keys: `AzureDevOps:Mode` (`AzureDevOps` default | `Local`), `AzureDevOps:LocalRoot`, `AzureDevOps:LocalPullRequestsRoot` (default: a `prs` folder beside the `LocalRoot` folder, i.e. `.local/prs` when `LocalRoot` is `.local/repos`); AppHost `LocalRepos:Enabled`, `LocalRepos:Root` (default `<repo root>/.local`), `Model:Enabled` (name kept from the prior branch).
- PR record: `baseCommit` = merge-base of target and source branch, `headCommit` = source branch head. When `EnsurePullRequestAsync` finds the existing active record, it refreshes those two fields (and `updatedAtUtc`) but keeps `id`, `url` and `createdAtUtc`. The diff view therefore shows the PR's current state, as an ADO PR would, not a per-run snapshot.
- PR diff form: full before/after content per file (the proposal panel's form), not a unified-diff renderer. 256 KB cap per side, 200 files max, and binary files are named but not shown. The endpoint is `GET /api/runs/{id}/pull-request-diff`, linked as `RunDetailDto.PullRequestDiffUrl` (appended last, null outside local mode).
- `Model:Enabled` moves into the Ollama task (04) because both edit the same AppHost lines; task 05 adds only `LocalRepos:*`.
- Feed `NuGet.config` in demo repos uses an **absolute** path computed at generation time. A relative path would silently turn verification into `Skipped` (NU1301), as noted above.
- Settings profiles (global): `baseline` (Minor, no prerelease), `patch`, `major`, `prerelease` (Minor + allowPrerelease). Every profile uses the same patterns `Contoso.*`, `Fabrikam.*`, `Northwind.*`, `Tailspin.*`, ignore `Tailspin.Legacy.*`, and the absolute `.local/feed` path.

### Scenario catalogue
Package families on `.local/feed` (all `netstandard2.0`; API stable across versions unless stated):

| Package | Versions | Notes |
| --- | --- | --- |
| Contoso.Logging | 1.2.0, 1.2.1, 1.2.3 | patch-only family |
| Contoso.Http | 2.1.0, 2.1.3, 2.2.0, 2.4.1, 3.0.0 | 3.0.0 API-compatible |
| Northwind.Serialization | 1.4.0, 2.0.0 | 2.0.0 additive (compiles) |
| Northwind.Messaging | 3.2.0, 4.0.0 | 4.0.0 removes `MessageBus.Publish(string, string)`, adds `PublishAsync(Message)`: consumer gets CS1061 |
| Tailspin.Core | 1.0.0, 1.0.2 | |
| Tailspin.Legacy.Data | 1.0.0, 1.1.0 | matched by ignore glob |
| Fabrikam.Telemetry | 1.0.0, 1.0.1, 1.1.0-beta.2 | pre-release |
| Fabrikam.Data.Abstractions | 1.0.0, 2.0.0 | |
| Fabrikam.Data | 1.0.0 (→ Abstractions `[1.0.0,2.0.0)`), 1.2.0 (→ Abstractions `[2.0.0,3.0.0)`) | forces escalation |
| Fabrikam.Data.SqlServer | 1.0.0 (→ Data `[1.0.0,2.0.0)`), 1.2.0 (→ Data `[1.2.0,2.0.0)`) | |
| Fabrikam.Reporting | 1.0.0 (→ Abstractions `[1.0.0,2.0.0)`), 1.1.0 (→ Abstractions `[3.0.0,4.0.0)`, absent) | unresolvable range |
| AdventureWorks.Text | 1.0.0, 1.5.0 | not matched by any pattern |

Demo repos under `.local/repos` (org/project ignored locally; enroll as `local/scenarios/<name>`). Expected results per profile:

| Repo | Layout | Declares | baseline (Minor) | patch | major | prerelease |
| --- | --- | --- | --- | --- | --- | --- |
| patch-bump | CPM | Contoso.Logging 1.2.0 | → 1.2.3, Completed | same | same | same |
| minor-bump | per-csproj, 2 projects + .sln | Contoso.Http 2.1.0 (both) | → 2.4.1, both csproj edited | → 2.1.3 | → 3.0.0 | → 2.4.1 |
| major-compiles | CPM | Northwind.Serialization 1.4.0 | NoUpdates (policy cap) | NoUpdates | → 2.0.0, Completed | NoUpdates |
| major-breaking | CPM | Northwind.Messaging 3.2.0 | NoUpdates | NoUpdates | VerificationFailed, CS1061, 0 attempts, "no model" transcript, no branch | NoUpdates |
| up-to-date | per-csproj | Contoso.Logging 1.2.3 | NoUpdates; map shows UpToDate | same | same | same |
| no-matches | per-csproj | AdventureWorks.Text 1.0.0 | NoUpdates; nothing matched | same | same | same |
| ignored-package | CPM | Tailspin.Core 1.0.0, Tailspin.Legacy.Data 1.0.0 | Core → 1.0.2 only; Legacy shows Ignored | same | same | same |
| prerelease | CPM | Fabrikam.Telemetry 1.0.0 | → 1.0.1 | → 1.0.1 | → 1.0.1 | → 1.1.0-beta.2 |
| family-alignment | CPM | Fabrikam.Data.Abstractions, .Data, .Data.SqlServer all 1.0.0 | SqlServer, Data → 1.2.0 (matched); Abstractions → 2.0.0 collateral, beyond policy; Completed | Data/SqlServer outdated only outside patch band: NoUpdates | all → highest (Abstractions matched) | as baseline |
| restore-conflict | CPM | Fabrikam.Reporting 1.0.0, Fabrikam.Data.Abstractions 1.0.0 | Reporting → 1.1.0; VerificationFailed with NU diagnostic; AI loop not entered | NoUpdates | as baseline, plus Abstractions → 2.0.0 | as baseline |
| lock-file | CPM + `RestorePackagesWithLockFile` + committed `packages.lock.json` | Contoso.Logging 1.2.0 | → 1.2.3; pushed commit includes the regenerated lock file | same | same | same |
| held-review | CPM + branch `scenario/agent-fix` | Northwind.Messaging 3.2.0 | seeded AwaitingReview; scheduler records SkippedHeld | same | same | same |
| missing-branch | CPM, only `main`; enrolled with target `develop` | Contoso.Logging 1.2.0 | NoUpdates (mirrors ADO; known gap) | same | same | same |
| does-not-exist | no folder | — | NoUpdates (mirrors ADO; known gap) | same | same | same |
| disabled-repo | CPM; enrolled `Enabled=false` | Contoso.Logging 1.2.0 | no run created | same | same | same |

The developer confirms the exact outcomes marked "known gap", plus `restore-conflict` (NU1102 vs NU1605) and the `family-alignment` patch-profile cell, when executing the walkthrough. Any divergence is recorded as a finding and not fixed in this feature.

## Tasks
1. [todo] 01 LocalRepositoryClient and the Local mode switch
2. [todo] 02 Local folder feeds through resolver, dependency reader and NuGet.config
3. [todo] 03 Local-mode end-to-end runner tests
4. [todo] 04 Ollama as the model provider, with an optional model
5. [todo] 05 AppHost: local-repos mode
6. [todo] 06 Scenario generator: feed packages
7. [todo] 07 Scenario generator: demo repos and enrollment profiles
8. [todo] 08 Held-for-review scenario seeder
9. [todo] 09 Read-only pull request diff on the run detail page (local mode)
10. [todo] 10 Manual walkthrough doc and executed run

## Risks and open questions
- **Stale NuGet global packages cache.** Restore reuses `~/.nuget/packages/<id>/<version>` even if the nupkg on the feed changed. `contoso.core` from the spike is already cached. The generator must purge the cache folders for every id it produces (honouring `NUGET_PACKAGES`). Tests use unique package ids per run. A wrong cache entry otherwise shows up as a confusing hash mismatch (NU1403) or wrong API.
- **Relative feed paths silently downgrade verification.** A relative source in a demo repo's `NuGet.config` resolves against the temp workspace and fails with NU1301, which is classified as environmental, so the run ends `Skipped` and still pushes. Generated configs therefore use absolute paths. The walkthrough calls this out under troubleshooting.
- **git availability and global git config.** Local mode needs `git` on PATH for `api` and `remediation`. User-level settings (`commit.gpgsign`, hooks, `core.autocrlf`) can break or alter local commits. The client passes `-c commit.gpgsign=false` and `--no-verify`, and the generator sets `core.autocrlf=false` per demo repo and writes LF without a BOM (pwsh 7 `Set-Content` default; Windows PowerShell 5.1 is not supported).
- **Archive covers committed state only.** `git archive` ignores uncommitted edits in a demo repo, which is faithful to Azure DevOps but can surprise someone hand-editing a repo. Documented in the walkthrough.
- **Wrapper-folder heuristic.** `VerificationWorkspace` strips a single non-source top-level folder. Every demo and test repo must have at least one root-level file (`README.md`, `Directory.Packages.props` or a `.sln`).
- **nupkg dependency ranges.** Packing `Fabrikam.Data 1.2.0` with `[2.0.0,3.0.0)` needs `Abstractions 2.0.0` on the feed at pack time, so the generator packs in dependency order against a temporary `NuGet.config` that points at the output feed. `Fabrikam.Reporting 1.1.0` depends on a version that never exists, so it must be packed with restore of that reference suppressed or via a hand-written nuspec. The developer chooses the method and verifies the nuspec ranges after packing.
- **file:// PR links in the browser.** Chromium blocks navigation from an http page to `file://`. The link renders and can be copied, but clicking it may do nothing. Task 09's read-only "Pull request changes" panel is the in-browser way to see what the PR contains.
- **Global settings and profiles.** Switching profile changes behaviour for every repo. Re-running after a profile switch reuses the existing update branch, which exercises the PRD "base mismatch" known defect. The walkthrough recommends regenerating (one command) or deleting the update branches and `.local/prs` between profiles.
- **Seeding the held run needs Azurite connection strings** (`ConnectionStrings__tables` / `__blobs`) copied from the Aspire dashboard, the same friction `ReviewGateTests` has. **Open question (the user wants to discuss it):** should the seeder instead be an explicit-start Aspire project resource added only in local mode, which would get the connection strings injected? The plan currently assumes env vars because it is smaller. Task 08 should not start until this is settled.
- **Deployed model after the Ollama swap.** `infra/terraform` still provisions an Azure AI Foundry account and sets `Agents__FoundryEndpoint`, and the Agents client becomes Ollama-only. Deployed apps don't connect to the model today anyway (the Terraform README lists the follow-up), so nothing that works breaks, but the deployed model story is undecided. **Open question:** how should the deployed environment get a model (Ollama-compatible endpoint, or a provider switch in `AddAgents`)? Task 04 writes "to be decided" in the README and doesn't touch `infra/`.
- **Aspire version alignment.** `CommunityToolkit.Aspire.*` 13.5.0 was used on the branch with Aspire 13.5.3; this branch is on 13.5.1 and also has `Aspire.Hosting.JavaScript`. Task 04 tries 13.5.1 first and bumps every Aspire package together only if restore requires it.
- **Ollama first start.** With the model enabled, the first `aspire start` pulls `phi4` (several GB) into the data volume. Until the pull finishes the model answers 404; task 04 ports the soft-dependency catch so that costs a repair, not a run. The walkthrough runs with the model disabled.
- **PR diff shows the PR's current state.** Because the record is refreshed on each push, an older run's diff panel shows the latest head, not that run's push. After regeneration, old runs' commits no longer exist and the panel reports the diff as unavailable. Both are documented in the walkthrough.
- **Task 09 size.** It spans Infrastructure, API, Contracts and Web. If review proves heavy, split it into 09a (reader, endpoint, DTO and tests) and 09b (UI, functional and axe tests).
- **Run duration.** Each scenario restores and builds (about 20–40 s), and the worker is sequential, so one scheduler pass over ~13 repos takes several minutes. The config script supports enrolling a subset.
