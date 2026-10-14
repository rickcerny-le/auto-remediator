# Self-provisioning local scenarios

**Status:** todo
**Created:** 2026-10-14

## Goal
Make the local test scenarios effectively permanent. Today the scenario definitions are committed, but the human has to run `New-LocalScenarios.ps1` and `Set-LocalScenarioConfig.ps1` by hand, and the output in `.local/` is gitignored and changed by runs. Azurite is not persisted either, so enrollment is lost on every start. After this feature, `LocalRepos__Enabled=true` plus `aspire start` is the whole quick start. The AppHost builds `.local` when it is missing or stale (and skips the ~85 s rebuild when it is current), resets it to a clean state, enrolls a settings profile through the API, and only then lets the scheduler run its startup pass. A fast reset is available between profiles without a rebuild.

## Scope
**In:**
- Fingerprinting in the generator. A hash of the generator inputs plus the absolute root and the .NET SDK version is written to `.local` after a successful build. A structural intactness check decides whether to skip or rebuild. `-Force` always rebuilds. An exclusive lock stops two generators from running at once.
- A fast reset (`New-LocalScenarios.ps1 -Reset`) that deletes the update branches, prunes worktrees, cleans the working trees, puts every repo back on `main` and empties `.local/prs`. It never rebuilds.
- A small console wrapper, `tools/local-scenarios/LocalScenarioSetup`, that runs as Aspire project resources. It checks that the required tools are on PATH and fails with a clear message if one is missing, runs the PowerShell scripts with their output streamed to the resource logs, and resolves the API URL through Aspire service discovery.
- AppHost wiring, local mode only:
  - `local-scenarios` (provision, runs at startup). `api`, `scheduler`, `remediation` and `held-run-seeder` wait for it to complete.
  - `local-scenarios-reset` (explicit start).
  - `local-scenarios-enroll` (runs at startup after `api` is healthy). `scheduler` waits for it to complete.
- The profile comes from `LocalRepos__Profile` (`baseline` by default). Reset-on-start can be turned off with `LocalRepos__ResetOnStart=false`.
- Docs: `docs/local-testing.md`, where the quick start becomes "set env vars, `aspire start`" and the expected-results matrix and findings are kept. Also `tools/local-scenarios/README.md` and the root README's local-mode section.
- Tests: a scripted fingerprint self-test (no Pester) and xUnit tests for the wrapper's pure logic. A verification task runs the cold-start and warm-start paths and records timings.

**Out:**
- Porting the generator to C# (a possible later feature).
- Committing built nupkgs or git bundles.
- An Azurite data volume (persisting run history).
- Baking scenarios into a container image.
- The deployed Foundry/Ollama revamp (a later feature).
- Fixing the 002 findings listed in `docs/local-testing.md`.
- An `-Only` subset at startup (`Set-LocalScenarioConfig.ps1 -Only` stays manual).
- Per-repo settings.
- Any change to the `held-run-seeder` behaviour (it stays explicit-start). Its only change is that it waits for provisioning.

## Acceptance criteria
- [ ] Cold start: with `.local/` deleted, `$env:LocalRepos__Enabled='true'; $env:Model__Enabled='false'; aspire start` builds `.local` (feed, repos, prs, fingerprint). `api`, `scheduler` and `remediation` stay `Waiting` until `local-scenarios` has `Finished` with exit code 0. The scenario repos are then enrolled under `baseline`, the scheduler runs its pass, and the baseline matrix in `docs/local-testing.md` is reproduced. No manual script is run.
- [ ] Warm start: `aspire stop` then `aspire start` with nothing changed skips the rebuild. The `local-scenarios` logs say so, and it finishes in under 15 s (the actual time is recorded). By default it also fast-resets the repos and `.local/prs`.
- [ ] A change to `scenarios.psd1`, `LocalScenarios.psm1` or `New-LocalScenarios.ps1`, a different absolute root, a different .NET SDK version, a missing or corrupt fingerprint file, or a structurally incomplete `.local` causes a rebuild on the next start. A line-ending-only change (CRLF and LF) does not.
- [ ] `pwsh tools/local-scenarios/New-LocalScenarios.ps1 -Force` always rebuilds. Without `-Force` it skips when the output is current. `-Reset` performs only the fast reset and leaves the feed, `main`, `scenario/agent-fix`, the fingerprint file, the NuGet cache and Azurite untouched.
- [ ] Starting `local-scenarios-reset` from the dashboard (or `aspire resource local-scenarios-reset start`) performs the same fast reset, and `Test-ScenarioRepos` passes afterwards.
- [ ] `LocalRepos__Profile=major` (or `patch` / `prerelease`) before `aspire start` enrolls that profile. An invalid value makes `local-scenarios-enroll` fail with a message that lists the valid values, while `api`, `web` and `remediation` keep running.
- [ ] If `pwsh`, `git` or `dotnet` is missing from PATH, the failing resource's console log names the missing tool and what to install. Nothing reads a half-built `.local`.
- [ ] With `LocalRepos__Enabled` unset, the AppHost composes exactly the same resources as before: no `local-scenarios*` resources and no health-check change on `api`.
- [ ] `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass, including the new wrapper tests. `pwsh tools/local-scenarios/Test-LocalScenarioFingerprint.ps1` passes.
- [ ] `docs/local-testing.md` and both READMEs describe the new flow, keep the expected-results matrix and the 002 findings, and record the measured cold and warm timings.

## Context
**Current state (read before starting):**
- `tools/local-scenarios/New-LocalScenarios.ps1` always deletes and recreates `<Root>/feed`, `repos` and `prs`. It calls `New-ScenarioFeed` (which purges the NuGet global-packages folders for the generated ids before and after packing), `New-ScenarioRepos` (which builds every repo as a self-check, then runs `git clean -xdf`) and `Test-ScenarioFeed`. `Test-ScenarioRepos` checks that HEAD is `main`, `main` has 1 commit, the working tree is clean, the `NuGet.config` holds the absolute feed path, and the branches are exactly `main` (plus `scenario/agent-fix` for `held-review`). It is called inside `New-ScenarioRepos` and is not exported to a separate check.
- `Set-LocalScenarioConfig.ps1 -ApiUrl <url> -Profile <p> [-Only] [-Root]` upserts the repos by name and `PUT`s `/api/settings`. It is idempotent.
- `tools/local-scenarios/HeldRunSeeder` is the pattern to follow: a console project referenced by `src/AutoRemediator.AppHost/AutoRemediator.AppHost.csproj`, added to `AutoRemediator.sln` under the `tools` solution folder, and registered in `AppHost.cs` inside `if (localReposEnabled)` with `WithExplicitStart()` and the `WithLocalRepos` helper.
- `AppHost.cs` computes `localRoot` from `LocalRepos:Root` (default `<AppHostDirectory>/../../.local`, made absolute with `Path.GetFullPath`). `api`, `scheduler` and `remediation` currently `WaitFor(storage)` and `WaitFor(serviceBus)` only. `api` has no `WithHttpHealthCheck`, so `WaitFor(api)` today waits only for `Running`. `/health` is mapped in Development by `ServiceDefaults.MapDefaultEndpoints`.
- `SchedulerWorker` runs one pass at startup and then exits. If enrollment completes before the scheduler starts, that pass covers the scenarios.
- The update branch name is `RemediationRunner.UpdateBranch` = `autoremediator/dependency-updates`. `LocalRepositoryClient` pushes through temporary worktrees under `%TEMP%\autoremediator-wt-*` and prunes them. Worktrees left behind by a killed process are cleared by `git worktree prune`.
- `.gitattributes`: `* text=auto`, `*.ps1 text eol=crlf`. The working copies of `.ps1` files are CRLF, while `.psm1` and `.psd1` follow the checkout's EOL settings. The fingerprint must therefore hash content with line endings normalised to LF and any BOM stripped.
- A gitignored root `nuget.config` (private feed and a disabled `LocalDev` source, with source mapping) sits above `.local`. The generated demo-repo `NuGet.config` files `<clear/>` both the sources and the source mapping, so the root file does not leak in. Keep it that way.

**Design (planner choices, Benny to log):**
- **Invocation:** the AppHost does not run `pwsh` directly with `AddExecutable`. A command that is not found then shows up as an opaque DCP start failure. Instead, a small net10.0 console project, `tools/local-scenarios/LocalScenarioSetup`, runs as three project resources with different arguments: `provision`, `reset` and `enroll`. Each mode first checks that its tools are on PATH: `provision` needs `pwsh`, `git` and `dotnet`; `reset` needs `pwsh` and `git`; `enroll` needs `pwsh`. It honours PATHEXT on Windows. If a tool is missing, it writes one clear line to stderr ("LocalRepos is enabled but `pwsh` (PowerShell 7) was not found on PATH. Install it, then `aspire stop` / `aspire start`.") and exits 2. Otherwise it runs `pwsh -NoProfile -NonInteractive -File <script> ...`, streams stdout and stderr to the console (so they appear in the resource logs) and returns the script's exit code. The AppHost passes absolute paths for the scripts folder and the root, so nothing is resolved by walking up directories.
- **Fingerprint (PowerShell, one source of truth):**
  - Lives in `LocalScenarios.psm1`, so the manual script and the AppHost share it.
  - SHA-256 over: a `FingerprintVersion` constant; the LF-normalised, BOM-stripped content of `scenarios.psd1`, `LocalScenarios.psm1` and `New-LocalScenarios.ps1`; the absolute root (lower-cased on Windows); and `dotnet --version`.
  - Written to `<Root>/scenarios.fingerprint.json` as the last step of a successful build, together with the inputs for diagnosis. It is deleted as the first step of a rebuild, so an interrupted or killed rebuild is never treated as current.
  - **Intact** is structural only:
    - the fingerprint file parses and matches;
    - the feed's nupkg set equals the catalogue's;
    - every non-`NoFolder` repo is a git repo with a `main` branch, and `held-review` has `scenario/agent-fix`;
    - no `NoFolder` repo exists.
  - Intact does not compare commit ids, so hand commits to `main` survive warm starts. They are wiped only by a rebuild.
- **Script modes:** `New-LocalScenarios.ps1` has four modes:
  - The default rebuilds when the output is stale and otherwise prints "up to date" and exits 0. This changes 002's always-rebuild default.
  - `-Force` always rebuilds.
  - `-Reset` does only the fast reset. If `.local` is not intact it refuses and suggests `-Force`.
  - `-ResetIfCurrent` is the default mode, plus a fast reset when it skips. The AppHost uses it.
  - All modes hold an exclusive OS file lock on `<Root>/.generator.lock` (`FileShare.None`, released automatically when the process dies). A second concurrent invocation fails fast and names the lock.
- **Fast reset:** for each catalogue repo folder:
  1. Run `git worktree prune`.
  2. Run `git checkout -f main`.
  3. Run `git reset --hard main`.
  4. Run `git clean -xdf`.
  5. Delete every local branch matching `autoremediator/*`.

  Then empty `<Root>/prs`. It must not touch:
  - `<Root>/feed` or the fingerprint file
  - the commits on `main` or `scenario/agent-fix`
  - any other branch
  - the NuGet global cache
  - Azurite (run history stays, and old runs' diff links 404 as in finding 4)
  - anything outside `<Root>`
- **Startup ordering (local mode only):**
  - `local-scenarios` has no dependencies, so it runs in parallel with the emulators' ~1 min start.
  - `api`, `scheduler`, `remediation` and `held-run-seeder` get `.WaitForCompletion(localScenarios)`.
  - `api` gets `.WithHttpHealthCheck("/health")`, in local mode only, so the default composition is unchanged.
  - `local-scenarios-enroll` gets `.WithReference(api).WaitFor(api)`. It reads `services:api:http:0` (falling back to `https`), polls `/health` for up to 2 minutes as a guard, then runs `Set-LocalScenarioConfig.ps1 -ApiUrl <url> -Profile <p> -Root <root>`.
  - `scheduler` additionally gets `.WaitForCompletion(enroll)`, so its startup pass sees the enrolled repos. If enrollment fails, `scheduler` does not start, but `api`, `web` and `remediation` keep running. To recover, fix the cause, start `local-scenarios-enroll` again from the dashboard, then start `scheduler`.
  - `local-scenarios-reset` is explicit-start and waits for `local-scenarios` to complete.
- **Config keys (AppHost):** `LocalRepos:Profile` (`baseline` | `patch` | `major` | `prerelease`, default `baseline`) and `LocalRepos:ResetOnStart` (default `true`; if `false`, the provision resource runs the default mode instead of `-ResetIfCurrent`). Both are read like the existing `LocalRepos:*` keys and printed in the AppHost's startup line.

**Relevant decisions (decisions.md, 002):**
- The generator is committed PowerShell 7 under `tools/local-scenarios`. It writes to the gitignored `.local`, uses absolute feed paths and purges the NuGet cache for its own ids.
- Settings profiles are global.
- The held-review seeder is an explicit-start resource in local mode only.
- The git binary is allowed only in local mode.

## Tasks
1. [todo] 01 Generator fingerprint, skip/force modes and lock
2. [todo] 02 Fast reset mode
3. [todo] 03 LocalScenarioSetup wrapper (provision and reset) with tests
4. [todo] 04 AppHost: provisioning at startup and the reset resource
5. [todo] 05 Enrollment at startup and scheduler ordering
6. [todo] 06 Documentation
7. [todo] 07 Cold/warm start verification with timings

## Risks and open questions
- **Open question: reset on every warm start.** The plan resets repos and `.local/prs` on every start by default (`LocalRepos__ResetOnStart=true`). Run history in Azurite is wiped on every start anyway, which leaves old branches and PR records orphaned. A clean start also reproduces the matrix exactly and avoids finding 1 (empty commits on re-runs). The cost is that the previous session's branches can no longer be inspected after a restart. Please confirm the default, or flip it.
- **Open question: an automatic scheduler pass on every start.** Because `scheduler` now waits for enrollment, every `aspire start` in local mode runs a full pass over the enabled scenario repos (about 1 to 2 minutes) and pushes branches. This is the existing "pass at startup" behaviour made useful. Confirm that this is wanted, or say whether the scheduler should only wait for enrollment when an opt-in flag is set.
- **Open question: the script's default mode changes.** In 002, `New-LocalScenarios.ps1` with no switches always rebuilt. The plan makes the default "rebuild only if stale", with `-Force` to rebuild unconditionally, as the request implies. Anyone used to the old behaviour needs `-Force`. The docs say so.
- **`WaitForCompletion` semantics.** A dependent of a resource that exits non-zero goes to `FailedToStart` rather than waiting forever. That is what is wanted here: nothing reads a half-built `.local`. Restarting `local-scenarios` from the dashboard while dependents are running re-runs the script under a live app. Task 04 confirms both behaviours on Aspire 13.5.1 first and documents them, and the docs say to `aspire stop` before `-Force`.
- **`aspire start` stops a previous instance.** A provisioning run that is killed mid-build leaves no fingerprint file (it is deleted first), so the next start rebuilds. A killed `dotnet build` or MSBuild node can, however, keep file handles on `.local/repos` on Windows, which makes the next delete fail. Task 01 passes `--disable-build-servers` (or shuts down the build servers) in the generator's dotnet calls and retries the folder delete briefly, with a clear message if it still fails.
- **NuGet cache purge.** The purge runs only on a rebuild. Warm starts keep the cache populated by earlier restores from the same feed bytes, which is consistent. If the fingerprint misses an input that changes nupkg content, a stale cache produces NU1403. That is why every generator file is in the fingerprint and `FingerprintVersion` exists for manual bumps.
- **Absolute paths.** The generated `NuGet.config` files and the fingerprint embed the absolute root. A moved or renamed checkout, or a different `LocalRepos__Root`, therefore triggers a rebuild, which is the intended behaviour. Drive-letter case is normalised so `C:` and `c:` do not differ.
- **Line endings.** `*.ps1` is CRLF on checkout, and the `.psm1` and `.psd1` files depend on the checkout's settings. Without normalisation, a re-checkout or an editor could trigger a needless 85 s rebuild. The self-test covers this.
- **Azurite is not persisted.** Enrollment must rerun on every start. It is idempotent by name, and Azurite is empty at startup, so it always creates.
- **Parallel runs.** Tests do not use `.local`, and Infrastructure tests use unique package ids and temp roots. Running `dotnet test` or `dotnet build` while the AppHost runs still hits `bin`/`obj` file locks (existing behaviour: `aspire stop` first). A manual `New-LocalScenarios.ps1` run while the AppHost is provisioning or resetting is stopped by the lock. A manual `-Reset` or `-Force` during a scheduler pass is not detected and can make in-flight runs fail. The docs say not to do it.
- **Script output in resource logs.** pwsh progress bars and ANSI colour clutter the dashboard logs. The scripts set `$ProgressPreference='SilentlyContinue'`, and `$PSStyle.OutputRendering='PlainText'` when output is redirected.
- **002 findings.** These are unchanged and out of scope (empty-commit re-runs, no-model wording, Approve clearing updates, stale diff-link 404s, NU1605 triple diagnostics, seeder cosmetics, missing-branch known defect). Reset-on-start makes finding 1 unlikely in normal use, but does not fix it.
