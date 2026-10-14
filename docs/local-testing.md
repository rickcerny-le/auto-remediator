# Local repository mode: manual integration test

This walkthrough runs the whole system on one machine against git repositories and a NuGet folder feed on
disk, with no Azure DevOps and (by default here) no model. It was executed once end to end on 2026-10-14; the
**Actual** columns and the [Findings](#findings) are what that run observed. Everything here is dev and test
only: local repository mode (`AzureDevOps:Mode=Local`) uses the `git` binary and is exempt from the product's
no-clone rule. Startup is automated: set two environment variables, run `aspire start`, and the scenarios are
built (or confirmed), enrolled and ready for a scheduler pass. See the [quick start](#1-quick-start).

## Prerequisites

- .NET 10 SDK
- Docker (Azurite and the Service Bus emulator always; Ollama only if the model is enabled)
- the Aspire CLI (`aspire`)
- PowerShell 7 (`pwsh`); Windows PowerShell 5.1 is not supported by the generator
- `git`

`pwsh`, `git` and `dotnet` must all be on the PATH that `aspire start` inherits: the AppHost starts the
`local-scenarios` resources, which look the tools up on that PATH (`dotnet` only for the provisioning step that can
rebuild; `git` is also needed by `api`, `remediation` and `held-run-seeder`). If one is missing the resource exits
with code 2 and its log says `LocalRepos is enabled but <tool> was not found on PATH.` followed by an install hint;
see [Troubleshooting](#troubleshooting).

## 1. Quick start

From a fresh clone, with Docker running:

```powershell
$env:LocalRepos__Enabled='true'; $env:Model__Enabled='false'; aspire start
```

Nothing else is needed: no script to run first and no enrollment step. The AppHost does this, in order:

1. **`local-scenarios`** builds or confirms the scenario data under `.local` (the NuGet folder feed, the demo git
   repos and the pull request folder). When the output is missing or stale it rebuilds (about 85 s cold); otherwise
   it skips the rebuild and does a fast reset (about 4 s). `api`, `remediation`, `scheduler` and `held-run-seeder`
   wait for it to finish, so nothing reads a half-built folder.
2. **`api`** and **`remediation`** start once the emulators are healthy **and** `local-scenarios` has finished: about
   40 s after `aspire start` returns on a warm start, and about 90 s after it returns on a cold start (the rebuild is
   the longer wait; `api` came up 2 s after it finished). `web` waits for `api`.
3. **`local-scenarios-enroll`** waits for `api` to pass its `/health` check, then enrolls every scenario repo and
   applies the settings profile (`baseline` unless you set `LocalRepos__Profile`). It finishes in under a second.
   A default start enrolls 15 repositories: 14 enabled plus `disabled-repo`, which is enrolled disabled.
4. **`scheduler`** does **not** start by itself in local mode. When the resources are `Running` and enrollment has
   finished, start a pass over every enabled repo:

   ```powershell
   aspire resource scheduler start
   aspire logs remediation        # per-run progress (verbose: Service Bus receiver logging)
   ```

   (or the Start button on `scheduler` in the dashboard). Set `LocalRepos__RunOnStart=true` to make the scheduler
   wait for enrollment and run the pass automatically instead.

Watch it with `aspire describe` (re-run until `api`, `remediation` and `web` show `Running`), `aspire logs
local-scenarios` and `aspire logs local-scenarios-enroll`. In `aspire describe` the resource names carry a random
suffix, but `aspire logs <short name>` works with the short names used here. Then open the web URL and the Runs page.

Where to find the URLs and connection strings:

- **API URL**: the `api` resource's `http` url (here `http://localhost:5228`).
- **Web URL**: the `web` resource's `http` url (here `http://localhost:53334`). Use this one, not the `PORT` env var.
- **Azurite connection strings** (only needed for the held-run seeder and the functional tests):
  `aspire describe --format Json` lists each resource's `environment`; take `ConnectionStrings__tables` and
  `ConnectionStrings__blobs` from the `api` entry. They address Azurite as `127.0.0.1`. Do not substitute the
  `TABLES_URI` / `BLOBS_URI` values, which say `localhost` (that can resolve to IPv6 and miss Azurite). Note that
  `aspire describe --format Json` writes UTF-16 when redirected to a file.
- The dashboard URL (with login token) is printed by `aspire start`.

With the AppHost in local mode, `api`, `scheduler`, `remediation` and the seeder all get `AzureDevOps__Mode=Local`,
`AzureDevOps__LocalRoot=<root>/repos` and `AzureDevOps__LocalPullRequestsRoot=<root>/prs` (`<root>` is `.local` in the
repo unless you set `LocalRepos__Root`). No PAT is needed. Azurite data is not persisted between `aspire start`
runs, so run history starts empty each time, which is why enrollment runs on every start.

## 2. How startup works

### Configuration

Set these as environment variables before `aspire start`.

| Variable | Default | Meaning |
| --- | --- | --- |
| `LocalRepos__Enabled` | `false` | Turns on local repository mode and the resources described here. |
| `LocalRepos__Root` | `<repo>/.local` | Folder holding `feed`, `repos` and `prs`. Used as an absolute path; a different root has its own fingerprint. |
| `LocalRepos__Profile` | `baseline` | Settings profile enrolled at startup: `baseline`, `patch`, `major` or `prerelease` (lower case). |
| `LocalRepos__ResetOnStart` | `true` | When the output is current, also run the fast reset on every start. `false` skips it (a rebuild still happens when stale). |
| `LocalRepos__RunOnStart` | `false` | `true` makes `scheduler` wait for enrollment and start automatically. |
| `Model__Enabled` | `true` | `false` leaves out Ollama and the model; see [section 3](#3-non-agentic-mode-the-model-is-disabled). |

An unparseable boolean counts as the default. The profiles are the table in [section 4](#4-enroll-a-profile-manually).

### Resources

| Resource | Start | What it does |
| --- | --- | --- |
| `local-scenarios` | automatic | Provision: rebuild when stale, otherwise skip, then fast reset unless `ResetOnStart=false`. |
| `local-scenarios-reset` | explicit | The fast reset only (see [Resetting between profiles](#resetting-between-profiles)). |
| `local-scenarios-enroll` | automatic, after `api` is healthy | Enrolls the repos and applies `LocalRepos__Profile`. |
| `scheduler` | explicit (unless `RunOnStart=true`) | Runs one pass over every enabled repo, then exits. |
| `held-run-seeder` | explicit | Seeds the held run for [the review gate](#8-review-gate). |

All three `local-scenarios*` resources run the `LocalScenarioSetup` tool, which calls the PowerShell scripts in
`tools/local-scenarios`. Its exit code is the script's exit code: `0` success, `1` failure (a failed self-check, an
invalid profile, the API not healthy within 120 s, stale output for a reset), `2` a required tool is missing from PATH,
`3` another generator holds the lock. Aspire shows a non-zero exit as `Finished <code>`, resources that wait for a failed
resource go to `FailedToStart`, and `web` stays `Waiting` if `api` failed.

### When it rebuilds

The generator writes `<root>/scenarios.fingerprint.json` after a successful build. On the next start it rebuilds only
if the fingerprint differs or the output is structurally incomplete. The fingerprint is a SHA-256 of:

- the generator files: `New-LocalScenarios.ps1`, `LocalScenarios.psm1` and `scenarios.psd1`
- the absolute root path (so a moved checkout rebuilds, because every repo's `NuGet.config` holds the absolute feed path)
- the .NET SDK version (`dotnet --version`)

Line endings and a BOM do not count, so a CRLF/LF checkout difference does not trigger a rebuild. The structural check
needs every nupkg of the catalogue, no extra nupkg, every demo repo with a `main` branch (and `held-review` with
`scenario/agent-fix`), no folder for `does-not-exist`, and a `prs` folder. Commit ids are not compared: a hand commit
to a demo repo's `main` survives warm starts (the fast reset keeps `main`), but a rebuild discards it. A rebuild is
roughly 85 s cold; a warm start (skip plus reset) takes about 4 s (3.7 to 4.2 s measured). A rebuild also purges the generated package ids
from the NuGet global packages cache (`~/.nuget/packages`, or `NUGET_PACKAGES`), so a stale cached package cannot give a
wrong API or an NU1403 hash mismatch.

### Forcing a rebuild

```powershell
aspire stop
pwsh -NoProfile -File tools/local-scenarios/New-LocalScenarios.ps1 -Force
aspire start
```

Stop first. The generator takes an exclusive lock on `<root>/.generator.lock` (a second generator exits with code 3),
and a rebuild deletes `feed`, `repos` and `prs`, which pulls them out from under any run in flight (the runs then end
with wrong results; no "files in use" failure showed up in the one trial on Windows, but do not rely on that). Run without `-Force`, the script now skips when the output is current. Add `-Root <path>`
to match a custom `LocalRepos__Root`. See [the script reference](../tools/local-scenarios/README.md) for all modes.

### Timings

Measured on 2026-10-14 on an Intel Core Ultra 7 265H (16 logical cores, 31 GB RAM), Windows 11, Docker running, .NET
SDK 10.0.300, git and pwsh 7.6.6. Seconds are from the timestamps in `aspire logs`, with `aspire start` timed from
invoking it (it returns after 10 to 16 s, with the AppHost started in the background).

| What | Seconds | Notes |
| --- | --- | --- |
| cold `local-scenarios` (rebuild) | 84.9 (also 84.4; 85.0 and 85.2 for the two invalidation rebuilds) | "Feed ready: 29 packages in 85s" |
| warm `local-scenarios` (skip + reset) | 4.1 (also 3.7 and 4.2) | reset itself 2.0 to 2.8 s, 6 or 7 update branches deleted; one start failed in the reset (finding 8) |
| warm without reset (`ResetOnStart=false`) | 1.2 | skip only; branches persist |
| `local-scenarios-reset` resource | 2.9 | the reset step alone, feed and fingerprint hashes unchanged |
| `local-scenarios-enroll` | 0.7 to 0.8 | wrapper time; resource `Running` 1.6 s after `api` was healthy |
| `aspire start` to `api` `Running`, cold | about 102 | waits for the 85 s rebuild; `api` 2 s after it |
| `aspire start` to `api` `Running`, warm | about 49 | waits for the emulators, not for the 4 s provision |
| `aspire start` to enrollment finished, cold / warm | about 105 / about 55 to 60 | |
| `aspire start` to first run finished, cold (`RunOnStart=true`) | 101.4 | first run started at +101.0 |
| `aspire start` to first run finished, warm (`RunOnStart=true`) | 63.3 | first run started at +59.5; last of 14 finished at +84.6 |
| scheduler pass, 14 repos | 27 to 31 | from the scheduler start to the last run; `minor-bump` is 10 to 16 s of that |

The cold start came in at the 85 s baseline (nothing slower), and the warm provision is well under the 15 s target.
With `LocalRepos__RunOnStart=true` and nothing else to do, the whole sequence from nothing is about 2 minutes 10 s
cold (the last run's finish was not timed in that cold session; estimated from the 31 s pass) and 1 minute 25 s warm.

## Self-provisioning check (actual)

Executed once on 2026-10-14 (task 07), `LocalRepos__Enabled=true`, `Model__Enabled=false`, every step below through
`aspire start` / `aspire stop` (never `dotnet run` on the AppHost). Run ids are the first 8 characters.

1. **Automated checks** (AppHost stopped): `dotnet build AutoRemediator.sln` 0 errors (1 warning, ASPIRE010).
   `dotnet test AutoRemediator.sln`: Tools 37 passed, Domain 66, Workers 13, Api 35, Agents 26 passed + 2 skipped,
   Infrastructure 211 passed + 16 skipped (2 m 19 s), Web.FunctionalTests 30 skipped (no `WEB_BASE_URL`, as designed). No
   failures. `Test-LocalScenarioFingerprint.ps1`: 34 PASS, 0 FAIL, "All cases passed".
2. **Cold start** (`.local` deleted): `local-scenarios` logged "Rebuilding local scenarios: scenarios.fingerprint.json
   is missing", purged 13 package ids, built 29 packages and 14 repos, exit 0 in 84.9 s. `api` `Running` 2 s after that,
   enrollment 0.8 s, created 15 repos (14 enabled, `disabled-repo` disabled), profile baseline. Scheduler started by
   hand 2 m after the start, first run 17:11:35, last run finished 17:12:04 (29 s). Baseline matrix spot check, from
   `/api/runs/<id>` and git: `patch-bump` (6e5861db) Completed, Contoso.Logging 1.2.0 -> 1.2.3; `minor-bump` (943460b5)
   Completed, Contoso.Http 2.1.0 -> 2.4.1, diff = both csproj; `family-alignment` (23018d70) Completed, Data and
   SqlServer 1.2.0 Matched, Abstractions 2.0.0 Collateral and beyond policy; `restore-conflict` (d03e0af0)
   VerificationFailed, `NU1605`, DependencyFailure, no remediation, no branch; `lock-file` (6bcd116b) Completed, diff =
   `Directory.Packages.props` and `src/App/packages.lock.json`; `disabled-repo` has no run (14 runs for 15 repos),
   `major-compiles`, `major-breaking`, `up-to-date`, `no-matches`, `held-review`, `missing-branch`, `does-not-exist`
   NoUpdates, `ignored-package`, `prerelease` Completed. As expected.
3. **Warm start**: log says "Local scenarios are up to date (fingerprint c1c59ae2a1a9); skipping rebuild", then
   "<repo>: deleted autoremediator/dependency-updates" for six repos and "Reset 14 repos (6 update branches deleted, 6
   PR entries removed) in 2.8s", exit 0 in 4.1 s. After the new pass each of `patch-bump`, `minor-bump`,
   `family-alignment`, `lock-file` had exactly one commit on `main..autoremediator/dependency-updates` (timestamped in
   the new pass), and `.local/prs/<repo>/1.json` `createdAtUtc` was from the new pass (17:13 to 17:14, against 17:11 to
   17:12 before): recreated, not refreshed.
4. **`ResetOnStart=false`**: log "up to date ... skipping rebuild" and no reset, exit 0 in 1.2 s. After another pass the
   four repos above had **two** commits on the branch (the second is empty: `git diff` between them is empty) and the PR
   record's `headCommit` and `updatedAtUtc` moved on while `createdAtUtc` stayed: finding 1 reappears.
5. **Invalidation**: appending one CRLF line to `scenarios.psd1` (not committed): "Rebuilding local scenarios: the
   generator inputs, root or .NET SDK changed (fingerprint mismatch)", 85.0 s. Reverting the file (hash identical to the
   original): the next start rebuilt again with the same reason, 85.2 s. The start after that logged "up to date
   ... skipping rebuild". See finding 8 for what happened next in that start.
6. **Profiles**: `major` (set at start): `major-compiles` (277be449) Completed, Northwind.Serialization 1.4.0 -> 2.0.0;
   `major-breaking` (fa41c58f) VerificationFailed, DependencyFailure, 0 attempts, transcript link present; `restore-conflict`
   (55cbd49f) VerificationFailed with 2 updates. `patch` and `prerelease` were applied to the running app (reset
   resource, `Set-LocalScenarioConfig.ps1 -Profile`, `scheduler restart`): patch gave `minor-bump` 2.1.0 -> 2.1.3
   (9d7b514d), `family-alignment` and `restore-conflict` NoUpdates, `prerelease` 1.0.0 -> 1.0.1 (1016196f), the rest as
   documented; prerelease gave `Fabrikam.Telemetry` -> 1.1.0-beta.2 (a0bf3218), `minor-bump` -> 2.4.1, `restore-conflict`
   VerificationFailed. All as the matrices above say. **Invalid profile** (`bogus`, with `RunOnStart=true`):
   `local-scenarios-enroll` `Finished 1` after 3 s with `LocalRepos__Profile 'bogus' is not valid; use baseline, patch,
   major or prerelease.` (no repositories were touched); `scheduler` `FailedToStart`; `api`, `web`, `remediation`
   `Running`.
7. **Reset resource**: `aspire resource local-scenarios-reset start` took 2.9 s (reset 2.0 s, 6 branches and 6 PR
   entries deleted). A SHA-256 over the feed files plus the fingerprint file was identical before and after, `.local/prs`
   was empty, only `main` was left in the demo repos. `start` on the finished resource works and re-runs it.
8. **Missing tool**: PATH stripped of every directory containing `pwsh*` (the WindowsApps PowerShell package dir and
   `%LOCALAPPDATA%\Microsoft\WindowsApps`): `local-scenarios` `Finished 2` in 3 s, log "LocalRepos is enabled but pwsh
   was not found on PATH. PowerShell 7 (pwsh) is required for local scenarios; Windows PowerShell 5.1 is not supported.
   Install it from https://aka.ms/powershell, then aspire stop / aspire start." `api`, `web`, `remediation`,
   `local-scenarios-enroll` `Waiting` (it ended the 20 s window there). Missing `git` or `dotnet` was not tried.
9. **Held review**: `aspire resource held-run-seeder start` with no manual enrollment: "Seeded held run 0a766eac-3c0e-441c-b43c-63865555fe46
   for local/scenarios/held-review (base e717ce65e6dd, 2 file(s))" and `Web: /runs/<id>`. Starting it again printed "A held run
   already exists ... 0a766eac" and created nothing. A scheduler pass with the held run present ran without error.
10. **Concurrency**: during the AppHost's cold rebuild (20 s in), `New-LocalScenarios.ps1` and `New-LocalScenarios.ps1
    -Force` by hand both exited 3 with "Another local-scenarios generator is running against <root> (lock
    <root>\.generator.lock). Wait for it to finish." With the lock held by another process: `-Reset` by hand exit 3;
    `local-scenarios-reset` `Finished 3` in 0.4 s; at start `local-scenarios` `Finished 3` in 0.4 s, `api` and
    `remediation` `FailedToStart` about 45 s later (when the emulators came up), `web` and enrollment `Waiting`,
    `scheduler` `NotStarted`.
11. **`RunOnStart`**: `true`: `scheduler` went `Running` the moment `local-scenarios-enroll` finished and `Finished 0` 2 s
    later; the 14 runs followed. Default: the scheduler stays `NotStarted` until `aspire resource scheduler start`.
12. **Functional suite** (after a clean baseline pass and a held-run-seeder start; `WEB_BASE_URL=http://localhost:63751`
    from `aspire describe --format Json`, `ConnectionStrings__tables`/`blobs` from the `api` environment at 127.0.0.1):
    **30 passed, 0 failed, 0 skipped** (33 s). The Configuration page (loaded in headless Chromium against the web
    URL, and `/api/repositories`) lists 15 repositories; only `disabled-repo` shows as Disabled, so 14 are enabled.

## Resource semantics observed

- `aspire resource <x> start` on a `Finished` resource (explicit or automatic) runs it again; the logs are replaced.
- `restart` on an explicit-start resource that has never run returns `NotFound` (404); use `start`.
- A resource that was `FailedToStart` stays so after the thing it waited on succeeds later; start it by hand.
- Non-zero exits show as `Finished <code>` (`2`, `3`, `1` all seen), the dashboard state shows the code, and
  `aspire describe --format Json` gives `exitCode`.
- `web` stays `Waiting` while `api` has not started or failed; `scheduler` with `RunOnStart=true` goes `FailedToStart`
  when enrollment fails or when `local-scenarios` had failed earlier in the same session.
- `aspire stop` normally took 0.8 s, once 30.9 s (right after an 85 s rebuild session).

## 3. Non-agentic mode (the model is disabled)

With `Model__Enabled=false` there is no model resource and `remediation` has no `ConnectionStrings__chat`. The
agent reports itself unavailable, so there is **no AI repair**:

- A bump that breaks the build ends `VerificationFailed` with **0 remediation attempts** and a transcript that says
  "No model is configured, so no repair was attempted."
- Nothing is pushed: no `autoremediator/dependency-updates` branch, no `.local/prs/<repo>` record, no PR link.
- The UI shows `VerificationFailed` in **amber** (warning tone), not red: the tool worked, the bump did not.
- Restore (`NU...`) failures never enter the repair loop in any mode.

Leaving the model enabled (the default) starts Ollama with `phi4`, which is a large first download, and makes
compile-break repairs possible. The expected results below assume the model is **disabled**.

## 4. Enroll a profile manually

Enrollment happens at startup (see [section 1](#1-quick-start)). Run the script yourself to switch profile against a
running app or to enroll only some repos with `-Only`:

```powershell
pwsh -NoProfile -File tools/local-scenarios/Set-LocalScenarioConfig.ps1 -ApiUrl http://localhost:5228 -Profile baseline
```

This lists, then creates or updates each repo by name (so it is safe to repeat) and applies the global targeting
settings for the profile. `-Only a,b` enables just those repos and disables the rest. The Configuration page in the
UI shows the same repositories and settings and can edit them (enter feed `C:\...\.local\feed`, patterns
`Contoso.*`, `Fabrikam.*`, `Northwind.*`, `Tailspin.*`, ignore `Tailspin.Legacy.*`).

| Profile | Strategy | Allow pre-release |
| --- | --- | --- |
| baseline | Minor | no |
| patch | Patch | no |
| major | Major | no |
| prerelease | Minor | yes |

`disabled-repo` is enrolled disabled, `missing-branch` targets `develop`, and `does-not-exist` has no folder.

## 5. Trigger runs

The scheduler runs once when it starts, then exits. In local mode it is an explicit-start resource, so the first pass
is a start and each later pass is a restart (or a start again: both work on a finished scheduler). `restart` on a
scheduler that has never been started fails (`Failed to restart resource 'scheduler': ... NotFound`), so use `start`
for the first pass:

```powershell
aspire resource scheduler start      # first pass after aspire start
aspire resource scheduler restart    # a pass after the scheduler has already run
aspire logs remediation        # per-run progress (verbose: Service Bus receiver logging)
```

(or the Start/Restart button on `scheduler` in the dashboard). A pass over all 14 enabled repos took about 30 s here
(27 to 31 s across several passes; `minor-bump` alone takes 10 to 16 s, the rest 0.1 to 4 s). The `scheduler` resource
itself shows `Finished 0` about 2 s after it starts, long before the runs finish: it only enqueues them, so follow
`aspire logs remediation` or the Runs page. Optionally set
`Scheduler__DevLoopEnabled=true` (and `Scheduler__DevLoopIntervalSeconds`) before `aspire start` to re-trigger
automatically.

## 6. What to observe

- **Runs page** (`/runs`): one row per run with status and a PR link for `Completed` runs.
- **Run detail** (`/runs/<id>`): the updates table (kind `matched` / `collateral`, and a beyond-policy flag),
  the verification outcome and diagnostics (capped on the run; the **full log** link has everything), the
  **transcript** link for runs that entered the repair loop (`major-breaking`), and the PR link.
- **Pull request changes** panel (read-only, local mode, `Completed` runs only): the files of the PR with before
  and after content, no action buttons. It should list exactly the files of
  `git -C .local/repos/<repo> diff --name-only main..autoremediator/dependency-updates`.
  The same data is `GET /api/runs/<id>/pull-request-diff`.
- **Dependency Map** (`/dependency-map`): `Outdated`, `UpToDate` or `Ignored` per package against the active
  profile. It is computed live from the repo and current settings, so it changes when you switch profile.
- Git state: `git -C .local/repos/<repo> log --oneline --all` and
  `git -C .local/repos/<repo> diff main..autoremediator/dependency-updates`.
- PR records: `.local/prs/<repo>/*.json` with `sourceBranch`, `targetBranch`, `baseCommit` (merge-base),
  `headCommit` (branch head), `status`, `url`. Re-running refreshes `headCommit` and `updatedAtUtc` but never
  creates a second record.

## 7. Expected results and actual results

Each repo, per profile. "Actual" is what was observed; run ids are the first 8 characters. `Completed` always means
the update branch exists, `.local/prs/<repo>/1.json` exists, a PR link is shown and verification was `Verified`.
Where the Actual column says "as expected" the outcome matched the expected cell exactly (statuses, versions and
flags were read from `/api/runs/<id>` and the git repo, not inferred).

### baseline (Minor)

| Repo | Expected | Actual |
| --- | --- | --- |
| patch-bump | Contoso.Logging 1.2.0 -> 1.2.3, Completed | as expected (67f700cb) |
| minor-bump | Contoso.Http 2.1.0 -> 2.4.1, both csproj edited, Completed | as expected (2295de34); diff = `src/Api/Api.csproj`, `src/Worker/Worker.csproj` |
| major-compiles | NoUpdates (policy cap) | as expected (8da91461) |
| major-breaking | NoUpdates | as expected (7c7390bd) |
| up-to-date | NoUpdates, map UpToDate | as expected (4e5f2a2d) |
| no-matches | NoUpdates, nothing matched | as expected (4afdaae9); not in the map |
| ignored-package | Core -> 1.0.2 only, Legacy Ignored | as expected (ab5e4905); map shows `Tailspin.Legacy.Data` Ignored |
| prerelease | Fabrikam.Telemetry -> 1.0.1 | as expected (78c6975e) |
| family-alignment | SqlServer and Data -> 1.2.0 matched, Abstractions -> 2.0.0 collateral and beyond policy, Completed | as expected (b21e72a5): 3 updates, Abstractions `Collateral` + beyond policy, Verified |
| restore-conflict | Reporting -> 1.1.0, VerificationFailed with NU diagnostic, repair loop not entered | as expected (a22a4b14): `NU1605` x3 (a package downgrade, not NU1102), classification DependencyFailure, remediation null, no branch. See finding 5 |
| lock-file | -> 1.2.3, pushed commit includes the regenerated lock file | as expected (f36df522): commit changes `Directory.Packages.props` and `src/App/packages.lock.json` |
| held-review | seeded AwaitingReview; scheduler records SkippedHeld | see [Review gate](#8-review-gate). Un-seeded, the repo is a normal repo: NoUpdates (8a6909c8) |
| missing-branch | NoUpdates (known gap) | as expected (9977db7c): NoUpdates, no error. The PRD "missing branch" known defect behaves as written |
| does-not-exist | NoUpdates (known gap) | as expected (3fb456b3): NoUpdates, no error. Same known defect |
| disabled-repo | no run created | as expected: no run in any pass |

Re-running the scheduler without resetting produced no second PR record for any repo (see finding 1 for the one
side effect).

### patch

| Repo | Expected | Actual |
| --- | --- | --- |
| patch-bump | same (1.2.3) | as expected (ac9c2809) |
| minor-bump | Contoso.Http -> 2.1.3 | as expected (237c2ab6) |
| major-compiles / major-breaking | NoUpdates | as expected (b65ce8df / b28198c9) |
| up-to-date, no-matches | NoUpdates | as expected (c08f8bd5, 8464bc18) |
| ignored-package | Core -> 1.0.2 | as expected (c14bc7f4) |
| prerelease | -> 1.0.1 | as expected (1c51019c) |
| family-alignment | NoUpdates (to confirm) | confirmed NoUpdates (f4d9f81b) |
| restore-conflict | NoUpdates | as expected (7d79f515) |
| lock-file | -> 1.2.3 with lock file | as expected (8dab4f62) |
| held-review, missing-branch, does-not-exist | NoUpdates | as expected (f7791e93, 45e55140, 1e9fb8bd) |

### major

| Repo | Expected | Actual |
| --- | --- | --- |
| patch-bump | same | as expected (b99600d2) |
| minor-bump | Contoso.Http -> 3.0.0 | as expected (5220b4ee) |
| major-compiles | Northwind.Serialization -> 2.0.0, Completed | as expected (264d6a8e) |
| major-breaking | VerificationFailed, CS1061, 0 attempts, "no model" transcript, no branch | as expected (9bc6385b-c7b5-4bae-9fb4-69868b8738b5); see [the non-agentic check](#non-agentic-check-actual) |
| up-to-date, no-matches | NoUpdates | as expected (2973c0ff, 6049e34a) |
| ignored-package | Core -> 1.0.2 only | as expected (48b7ecf4) |
| prerelease | -> 1.0.1 | as expected (d49001c4) |
| family-alignment | all -> highest, Abstractions matched | as expected (271fd7cc): Abstractions 2.0.0 `Matched`, Data and SqlServer 1.2.0 |
| restore-conflict | as baseline plus Abstractions -> 2.0.0 | as expected (5d43038d): 2 updates, still `NU1605` x3 |
| lock-file | -> 1.2.3 with lock file | as expected (a8b07ef0) |
| held-review | (normal repo here) | VerificationFailed, CS1061, 0 attempts (1b1b26a1), same as `major-breaking`: it has the same code |
| missing-branch, does-not-exist | NoUpdates | as expected (9906e56e, 8718ca92) |

### prerelease (Minor + pre-release)

| Repo | Expected | Actual |
| --- | --- | --- |
| prerelease | Fabrikam.Telemetry -> 1.1.0-beta.2 | as expected (ab76b613) |
| patch-bump, ignored-package, lock-file | as baseline | as expected (aae86ee6, a2356652, ac504457) |
| minor-bump | -> 2.4.1 | as expected (2c13cc47) |
| major-compiles, major-breaking, up-to-date, no-matches | NoUpdates | as expected (664a06e0, 1c3a05c6, 002c02b3, a685c55a) |
| family-alignment | as baseline | as expected (a8d687e7): Abstractions collateral, beyond policy |
| restore-conflict | as baseline | as expected (a3bc419c): NU1605 |
| held-review, missing-branch, does-not-exist | NoUpdates | as expected (b8858864, 9e5d9121, afc6a3b2) |

### Non-agentic check (actual)

Under the `major` profile with `Model__Enabled=false`, `major-breaking` run
`9bc6385b-c7b5-4bae-9fb4-69868b8738b5`:

- status `VerificationFailed`; update `Northwind.Messaging 3.2.0 -> 4.0.0`
- verification `DependencyFailure` (shown as "rejected"), one diagnostic `CS1061` at `src/App/Program.cs:1:38`
  (`'MessageBus' does not contain a definition for 'Publish'`)
- remediation: 0 attempts; the transcript contains "No model is configured, so no repair was attempted."
- the status pill renders with the `warn` (amber) tone (`ar-pill warn`, confirmed in the rendered DOM and a screenshot)
- `git -C .local/repos/major-breaking branch -a` shows only `main`, there is no `.local/prs/major-breaking` folder
  and the run has no PR link

### PR diff panel check (actual)

For every `Completed` run in every pass, the file list from `/api/runs/<id>/pull-request-diff` equalled
`git diff --name-only main..autoremediator/dependency-updates` (for example `minor-bump`: `src/Api/Api.csproj`,
`src/Worker/Worker.csproj`; `lock-file`: `Directory.Packages.props`, `src/App/packages.lock.json`;
`held-review` after Approve: `Directory.Packages.props`, `src/App/Program.cs`). The page-level Playwright tests also
passed against these runs (see [Automated checks](#9-automated-checks-against-the-running-app)).

## Profiles and resetting between them

Settings are global, so switching profile changes behaviour for every repo. A re-run reuses the existing
`autoremediator/dependency-updates` branch and PR record, which is where the PRD "base mismatch in planning" known
defect can show up. So reset before each profile.

### Resetting between profiles

Reset the repos and pull request records without a rebuild (a few seconds), with either:

```powershell
aspire resource local-scenarios-reset start
# or, with the app stopped or idle:
pwsh -NoProfile -File tools/local-scenarios/New-LocalScenarios.ps1 -Reset
```

Then pick the profile in one of two ways:

- **Restart the AppHost** with the new profile. This is the simplest: a default start already resets (provisioning
  plus reset is about 4 s warm) and enrolls the profile.

  ```powershell
  aspire stop
  $env:LocalRepos__Profile='patch'; aspire start
  aspire resource scheduler start
  ```

- **Keep the app running**: apply the profile through the API, then run another pass.

  ```powershell
  aspire resource local-scenarios-reset start
  pwsh -NoProfile -File tools/local-scenarios/Set-LocalScenarioConfig.ps1 -ApiUrl http://localhost:5228 -Profile patch
  aspire resource scheduler restart
  ```

What a reset does: for every demo repo it prunes worktrees, puts the repo on `main`, discards uncommitted changes and
untracked files, and deletes every `autoremediator/*` branch; it also empties `<root>/prs`. What it never touches:
`main` itself (hand commits survive), `scenario/agent-fix` and any other branch you made, the `feed`, the fingerprint
file, anything outside the root, and Azurite (run history and the enrolled repositories stay). The reset exits with code
1 if the output is missing or stale; start the app (or run the script with no switch) to rebuild it first.

Do not reset while a scheduler pass is running. Nothing detects it: branches and PR records are deleted under the
runs in flight, so their results are not meaningful. Wait for `aspire logs remediation` to go quiet first. The same
applies to a manual `-Reset` or `-Force` run outside Aspire. Observed when tried on purpose (see finding 10): a `-Reset`
6 s into a pass exited 0 and left two `Completed` runs without a branch or PR record; a `-Force` 6 s into a pass also
exited 0 (86 s) and the runs that were still going ended `NoUpdates` or `VerificationFailed` against the rebuilt repos.

The walkthrough used the quick reset between profiles (after the first, freshly generated, pass). When profile
`baseline` was re-applied on top of the `patch` results **without** resetting, `minor-bump` still pushed the correct
2.4.1 content (the planner reads `main`, and the push overwrites the files), so the base-mismatch defect did not
show up in this scenario; it only added another commit to the branch.

## 8. Review gate

`AwaitingReview` normally needs a model to repair a compile break, so it is seeded. The seeder is an
explicit-start Aspire resource that exists only in local mode and gets the Azurite connection strings injected.

The repos are already enrolled at startup, so the seeder needs nothing first. Optionally enable only `held-review` so a
scheduler pass touches just that repo:

```powershell
pwsh -NoProfile -File tools/local-scenarios/Set-LocalScenarioConfig.ps1 -ApiUrl http://localhost:5228 -Only held-review   # optional
aspire resource held-run-seeder start
aspire logs held-run-seeder    # prints: Seeded held run <id> ... and Web: /runs/<id>
```

(The seeder's console output is dominated by Azure SDK HTTP logging; the two lines that matter are the last ones.)
The held run's base commit is `held-review`'s real `main` head and its proposal is the real difference between
`main` and `scenario/agent-fix` (`Directory.Packages.props` and `src/App/Program.cs`). It appears on Home and Runs
as Awaiting review. Starting the seeder again while a held run exists prints the existing run id and creates
nothing (verified: still one held run). The commands, sent here with the same `POST /api/runs/<id>/review/<command>`
the buttons use (HTTP 202 each):

| Action | Expected | Actual (run 590a8dc0 unless stated) |
| --- | --- | --- |
| Retry | blocked: no model configured | stays `AwaitingReview`, `reviewNote` "no model is configured, so retry could not run" |
| Rebuild | re-verify at the current head | stays `AwaitingReview`, note cleared, proposal `verifiedAtUtc` refreshed, `reviewCommandCount` 2 |
| Approve | push the stored proposal, open the PR | `Completed`, branch commit with both files, `.local/prs/held-review/1.json`, PR link and the "Pull request changes" panel listing both files. The run's `updates` list is empty afterwards (finding 3) |
| Discard | run closed, nothing pushed | run e19138d8: `Discarded`; the update branch from the Approve was left as it was |
| Scheduler pass while held | `SkippedHeld` | restart `scheduler` with a held run: `SkippedHeld` run ddc0e900 for `held-review` (others unaffected) |

After Approve or Discard the run is no longer held, so starting the seeder again seeds a new run (e19138d8, then
0bc07dee). To reset the scenario, start `local-scenarios-reset` (see [Resetting between profiles](#resetting-between-profiles)).

## 9. Automated checks against the running app

With a Completed local run (with a PR diff) and a held run present, run the functional suite, pointing at the app:

```powershell
$env:WEB_BASE_URL='http://localhost:53334'
$env:ConnectionStrings__tables='<from the api resource environment, 127.0.0.1>'
$env:ConnectionStrings__blobs='<same>'
dotnet test tests/AutoRemediator.Web.FunctionalTests
```

Result on the walkthrough machine: **30 passed, 0 failed, 0 skipped**, including `PullRequestDiffTests`,
`ReviewGateTests` (3), `FocusVisibleTests` (12, both themes and forced colors), the axe WCAG A/AA tests (both themes,
run detail with the PR diff panel included), `HomePageSmokeTests` (2). The suite finds "a run with a PR diff" as
the first run whose diff link is set; it passed here after a clean baseline pass (see troubleshooting for stale runs).

## Troubleshooting

- **`local-scenarios` failed (`Finished <code>`), or `api` and `remediation` show `FailedToStart`**: those resources
  wait for `local-scenarios` to complete, so they do not start when it exits non-zero (and `web` and
  `local-scenarios-enroll` stay `Waiting` because `api` did not start; `scheduler` stays `NotStarted`, or shows
  `FailedToStart` with `RunOnStart=true`). `api` and `remediation` only flip to `FailedToStart` once the emulators are
  up, about 45 s after the start. Read `aspire logs local-scenarios`: the last line says `finished with exit code <n>`.
  Fix the cause (below), then `aspire stop` and `aspire start`. Re-running just `aspire resource local-scenarios start`
  works (it re-runs the provision), but it does not rescue the dependents: they stay `FailedToStart` and you must start
  each by hand (`api`, `remediation`, then `scheduler`), so prefer the full restart.
- **A required tool is missing** (exit code 2): the log says `LocalRepos is enabled but <tool> was not found on PATH.`
  and an install hint. `provision` needs `pwsh`, `git` and `dotnet`; `reset` needs `pwsh` and `git`; `enroll` needs
  `pwsh`. Install it or fix PATH in the shell you run `aspire start` from, then `aspire stop` and `aspire start` so the
  AppHost inherits it.
- **The generator lock is held** (exit code 3): `Another local-scenarios generator is running against <root>`. Another
  `New-LocalScenarios.ps1` (or a reset) is still working on the same root. Wait for it, or `aspire stop` if it is an
  AppHost resource, then start again.
- **A folder is locked during a rebuild**: `Could not delete <path> ... A process is probably holding files in it`.
  Usually a build server or a stopped-but-not-exited process holds the files. `aspire stop`, run
  `dotnet build-server shutdown`, then start again (or run `New-LocalScenarios.ps1 -Force`).
- **Enrollment failed while the app is up**: only `local-scenarios-enroll` is affected, so `api`, `web` and
  `remediation` keep running. Read `aspire logs local-scenarios-enroll` (for example the API did not become healthy
  within 120 s), fix the cause, run `aspire resource local-scenarios-enroll start`, then start `scheduler`. If you set
  `LocalRepos__RunOnStart=true`, the scheduler did not start either (it waits for enrollment), so start it yourself
  after enrollment succeeds.
- **An invalid profile**: `LocalRepos__Profile '<value>' is not valid; use baseline, patch, major or prerelease.` The
  names are case-sensitive and lower case. Only `local-scenarios-enroll` fails (`Finished 1`); the rest of the app runs.
  Fix the variable and restart the AppHost, or enroll by hand with `Set-LocalScenarioConfig.ps1 -Profile <p>`.
- **Run ends `Skipped` with NU1301**: a feed path problem (source unreachable is classed as environmental, and the
  change would still be pushed "verification skipped"). The generated `NuGet.config` files use absolute paths; a
  relative path in a demo repo's `NuGet.config` breaks inside the temp workspace.
- **NU1403 or the wrong API**: a stale NuGet global packages cache. Rebuild with `New-LocalScenarios.ps1 -Force` (a rebuild purges the ids it makes).
- **"git was not found on PATH"**: install git or fix PATH, then restart the AppHost so `api` and `remediation`
  inherit it (see also the missing-tool entry above).
- **`file://` links do nothing in the browser**: Chromium blocks navigation from an http page to `file://`. Copy the
  link, or use the "Pull request changes" panel.
- **The "Pull request changes" panel is missing or says "not available: 404 Not Found"** for old runs: the panel shows
  the PR record's *current* state by repo, so a run whose repo has no PR record any more (after a reset or a
  regeneration) has a diff link that 404s, and its commits no longer exist after regeneration. Only the latest
  pass's runs are meaningful. Run id `264d6a8e` (a `major-compiles` run from an earlier pass) showed this.
- **Uncommitted edits to a demo repo are invisible**: verification and planning use `git archive` of committed
  state, as Azure DevOps would. Commit your edit to `main` (the generator's commit identity is not required).
- **Restarting a resource vs `aspire stop`**: `aspire resource <name> restart` restarts one resource and keeps
  Azurite data; `aspire stop` stops the whole AppHost and the Azurite and Service Bus emulator containers' data
  is not persisted, so run history is gone on the next `aspire start`. Always `aspire stop` before building the
  solution (a running AppHost holds file locks on `bin`/`obj`).
- **Run history surviving regeneration**: if Azurite is kept running while you regenerate, old runs still reference
  commits that no longer exist (see the 404 above). Restart the AppHost for a clean history.
- **Scheduler restarted but nothing happens**: the resource restart returns immediately; follow
  `aspire logs remediation`. A pass over 14 enabled repos took about 30 s. The scheduler does not start by itself
  (unless `LocalRepos__RunOnStart=true`): `aspire resource scheduler start`.

## Findings

Divergences and surprises from the executed walkthrough. None of these were fixed (out of scope).

1. **Re-running pushes an empty commit.** Re-running the scheduler without a reset, for a repo that already has the
   update branch, pushes a new commit with an identical tree onto `autoremediator/dependency-updates`
   (`patch-bump`: `1eced66` after `c5812dc`, no file difference; run `a89fedff-a847-4661-a417-855661952621`,
   status `Completed`, 1 update). The PR record is refreshed (`headCommit`, `updatedAtUtc`) and no second record
   is created, as designed, but the branch grows by an empty commit per pass.
2. **"AI remediation" wording is wrong for a no-model run.** Run `9bc6385b-c7b5-4bae-9fb4-69868b8738b5` (0 attempts,
   transcript "No model is configured") renders: "A compile break in this run was handed to an AI agent, which proposed
   source edits that were re-verified. The attempts did not produce a building tree, so no pull request was opened."
   No agent acted and nothing was proposed. The section also shows an info-toned "0 attempts" pill.
3. **Approve clears the run's `updates`.** Run `590a8dc0-f685-4113-ad5b-073351671598` listed
   `Northwind.Messaging 3.2.0 -> 4.0.0` while `AwaitingReview`; after Approve it is `Completed` with an empty `updates`
   list (so the Updates table disappears) and no `verification` object, so there is no "Local verification" section
   or log link for an approved run. Discard (run `e19138d8`) kept its update and also has no verification.
4. **Stale diff links 404.** `pullRequestDiffUrl` is non-null for every `Completed` local run, including ones whose PR
   record no longer exists (e.g. run `264d6a8e-f707-46d5-92e6-fbca2a6c82e8`); the page then shows "Pull request changes
   are not available: 404 Not Found". Expected by the design notes, but the message is terse.
5. **restore-conflict emits `NU1605`, and three diagnostics for one problem.** Under baseline (run
   `a22a4b14-6498-4622-b7d5-3e8713293d52`) and the other profiles that run it the restore failure is
   `NU1605` (package downgrade, Abstractions 3.0.0 -> 1.0.0), not `NU1102`. The one error appears as three
   `NU1605` diagnostics because the "App -> ..." dependency-chain continuation lines are parsed as separate
   diagnostics. Under `major` (run `5d43038d`) it still fails (Abstractions 2.0.0 vs required >= 3.0.0). The AI loop is
   not entered, as expected.
6. **Cosmetic, seeder.** The seeded held run's `startedAtUtc` is about 5 minutes before the time it was seeded
   (fake run times), and the seeder's console output is mostly Azure SDK HTTP logging.
7. **PRD "missing branch / unknown repository" known defect** behaves exactly as written: `missing-branch` (run
   `9977db7c`) and `does-not-exist` (run `3fb456b3`) end `NoUpdates` with no error.

8. **Transient reset failure right after a rebuild (task 07).** On the start immediately after the second rebuild
   (about 15 s after the rebuild finished, with an `aspire stop` and `aspire start` in between), `local-scenarios` logged
   "Local scenarios are up to date ... skipping rebuild", then `error: update_ref failed for ref 'HEAD': cannot update the
   ref 'HEAD': unable to append to '.git/logs/HEAD': Permission denied` and `Exception: git reset -q --hard main failed
   in ...\repos\restore-conflict (exit code 1)`, so the resource finished with exit 1 after 3.0 s and `api`,
   `remediation` went `FailedToStart`. A manual look afterwards found the file writable, and `aspire resource
   local-scenarios start` succeeded (2.9 s). Not reproduced in about ten other warm starts. The likely cause is a
   transient handle on a freshly written `.git` (antivirus or the previous session's processes), but it was not
   proven. A retry inside the reset (or in the wrapper) would stop it from taking the whole start down.
9. **Dependents do not recover.** After finding 8 (or any `local-scenarios` failure), a later successful run of
   `local-scenarios` does not release `api`, `remediation` or `scheduler` (they stay `FailedToStart`); each had to be
   started by hand. With `RunOnStart=true` the scheduler also stays failed, so the pass has to be started by hand.
10. **`-Reset` or `-Force` during a scheduler pass silently corrupts it.** The lock only guards generators, not the
    app. `-Reset` 6 s into a pass exited 0 and left `ignored-package` and `lock-file` runs `Completed` with no branch and
    no PR record (only 4 of 6 PR records existed). `-Force` 6 s into a pass exited 0 (86 s) and the later runs ended
    `NoUpdates` or `VerificationFailed` (`minor-bump`) against repos that were being replaced. Only documented, not
    detected. The earlier doc claim that `-Force` would "fail on files the running app holds" did not happen here.
11. **`restart` of a never-started explicit resource fails** with a 404 from the orchestrator (`scheduler`). The docs
    already said "start" for the first pass; this just confirms why.
12. **Cosmetic, enrollment failure.** For an invalid profile the last log line of `local-scenarios-enroll` is the
    "is not valid" message; unlike the provision failures there was no `enroll finished with exit code 1` line in the
    first lines of the tail that were read, though the resource shows `Finished 1`. Not investigated further.
13. **Doc claims corrected by this run.** "`api` starts about a minute after `aspire start` returns" (it waits for
    `local-scenarios`, so cold is about 90 s); "a pass takes 1 to 2 minutes" (about 30 s); "warm start about 3.5 s"
    (about 4 s); "`scheduler` shows `FailedToStart`" after a `local-scenarios` failure (it stays `NotStarted` unless
    `RunOnStart=true`); and the `restart` wording for the first pass.

Unverified: a missing `git` or `dotnet` on PATH (only `pwsh` was removed); the dashboard rendering of `Finished <code>`
(read from `aspire describe`, not the dashboard); that each `-Reset`/`-Force` effect is deterministic (one trial each).

No expected-results cell diverged (every status, version and flag in the matrices matched), so the findings above
are product or doc observations rather than matrix failures.
