# Local scenarios

PowerShell 7 tooling that builds realistic upgrade scenarios on disk for local repository mode. The full
walkthrough is [docs/local-testing.md](../../docs/local-testing.md); this file is usage only. Windows PowerShell 5.1
is not supported.

You normally do not run any of this by hand. With `LocalRepos__Enabled=true` the AppHost builds (or confirms),
resets and enrolls the scenarios itself through the `LocalScenarioSetup` tool; the scripts below are what it calls and
what you use to force a rebuild or re-apply the catalogue settings.

## New-LocalScenarios.ps1

```powershell
pwsh tools/local-scenarios/New-LocalScenarios.ps1 [-Root <path>] [-Force | -Reset | -ResetIfCurrent]
```

Builds the NuGet folder feed `<Root>/feed`, the demo repos `<Root>/repos` and the pull request records `<Root>/prs`
(default root: `<repo root>/.local`, which is gitignored). It packs the feed, then creates every demo repo in
`scenarios.psd1` as a git repo on `main` with one commit (fixed identity and dates, so commit ids are reproducible;
`lock-file` differs because its lock file hashes re-packed nupkgs). Each repo's `NuGet.config` points at the absolute
`<Root>/feed` path. The script builds every repo (and `held-review`'s `scenario/agent-fix` branch) as a self-check, so
it needs the .NET 10 SDK and `git`. A rebuild also purges the generated package ids from the NuGet global packages
cache. A cold rebuild takes about 85 s.

| Mode | Behaviour |
| --- | --- |
| (default) | Rebuilds only when the output is missing or stale; otherwise prints that it is up to date and exits 0 without touching anything. |
| `-Force` | Rebuilds even when current. Deletes and recreates `feed`, `repos` and `prs`, discarding hand commits. Stop the app first (`aspire stop`). |
| `-Reset` | Fast reset only, a few seconds, no rebuild: per repo prunes worktrees, checks out `main`, discards changes and untracked files and deletes `autoremediator/*` branches; empties `<Root>/prs`. Keeps `main`, `scenario/agent-fix`, other branches, the feed and the fingerprint. Exits 1 if the output is missing or stale. |
| `-ResetIfCurrent` | The default mode, plus the fast reset when the output is current. After a rebuild it does nothing more. This is what the AppHost's `local-scenarios` resource runs. |

`-Reset` cannot be combined with `-Force` or `-ResetIfCurrent` (exit 1). Do not run a reset or a rebuild while a
scheduler pass is running; it is not detected.

Exit codes: `0` success (including "up to date"), `1` a failed self-check, invalid switch combination or a reset of
missing/stale output, `3` another generator holds the lock.

**Staleness.** After a successful build the script writes `<Root>/scenarios.fingerprint.json`: a SHA-256 over
`New-LocalScenarios.ps1`, `LocalScenarios.psm1`, `scenarios.psd1`, the absolute root and the .NET SDK version (line
endings and a BOM are normalised away), plus a structural check (every nupkg present and no extra, every repo with a
`main` branch, `held-review` with `scenario/agent-fix`, no folder for `does-not-exist`, a `prs` folder). Commit ids are
not compared, so a hand commit to `main` survives a skip or a reset but not a rebuild. The file is deleted first on
every rebuild, so a killed run is never treated as current.

**Lock.** The script holds an exclusive lock on `<Root>/.generator.lock` while it works, so two generators cannot run at
once on one root; the second prints `Another local-scenarios generator is running against <root>` and exits 3.

Each repo's `README.md` states the scenario, its expected outcome and its settings (or "Inherits the global baseline").

## Test-LocalScenarioFingerprint.ps1

```powershell
pwsh tools/local-scenarios/Test-LocalScenarioFingerprint.ps1
```

A self-test for the fingerprint, the output-current check and the fast reset. No Pester and no build: it uses a fake
root in the temp folder and finishes in a few seconds. It prints one `PASS`/`FAIL` line per case and exits non-zero if
any case fails. Run it after changing `LocalScenarios.psm1` or `New-LocalScenarios.ps1`.

## LocalScenarioSetup

A small .NET tool (`tools/local-scenarios/LocalScenarioSetup`, run by the AppHost as an Aspire project resource) that
checks the required tools are on PATH, runs the matching script with `pwsh -NoProfile -NonInteractive` and streams its
output to the resource log. It ends with `local-scenarios: <mode> finished with exit code <n> in <s> s`.

```text
LocalScenarioSetup <provision|reset> --scripts <dir> --root <dir> [--no-reset]
LocalScenarioSetup enroll --scripts <dir> --root <dir>
```

| Mode | AppHost resource | Runs | Needs on PATH |
| --- | --- | --- | --- |
| `provision` | `local-scenarios` (automatic) | `New-LocalScenarios.ps1 -ResetIfCurrent` (`--no-reset`, from `LocalRepos__ResetOnStart=false`, drops the switch) | `pwsh`, `git`, `dotnet` |
| `reset` | `local-scenarios-reset` (explicit start) | `New-LocalScenarios.ps1 -Reset` | `pwsh`, `git` |
| `enroll` | `local-scenarios-enroll` (after `api` is healthy) | waits up to 120 s for the API's `/health`, then `Set-LocalScenarioConfig.ps1` against the API URL Aspire injects | `pwsh` |

Exit codes: `0` success; `1` the script failed, bad arguments (including the removed `--profile`) or the
API not healthy in 120 s; `2` a required tool is missing from PATH (the message names it and how to install it); `3`
the generator lock is held (the script's own exit code is passed through). Aspire shows a non-zero exit as
`Finished <code>`.

## Set-LocalScenarioConfig.ps1

```powershell
pwsh tools/local-scenarios/Set-LocalScenarioConfig.ps1 -ApiUrl http://localhost:<port> `
    [-Only <name>,<name>] [-Root <path>]
```

The AppHost runs this for you at startup (resource `local-scenarios-enroll`). Run it by hand to reset every repo
(and the global baseline) to the catalogue against a running app, or to use `-Only`. `LocalRepos__Profile` is obsolete
and ignored (the AppHost prints a notice).

It enrolls every scenario repo (organization `local`, project `scenarios`, target `main`; `missing-branch` targets
`develop`, `disabled-repo` is disabled; 16 rows, 15 enabled), sends each repo's `Settings` from `scenarios.psd1` and
applies the `GlobalSettings` baseline. Safe to run repeatedly: repos are matched by name and updated, never duplicated,
and every repo is reset to the catalogue (settings edited in the UI are overwritten). `-Only` enables just the named repos and disables the rest.

The global baseline (`GlobalSettings`) is patterns `Contoso.*`, `Fabrikam.*`, `Northwind.*`, `Tailspin.*`, the absolute
`<Root>/feed` path (`{feed}` in the catalogue), no excludes, no ignore, Minor, no pre-release and the built-in daily
schedule. A repo may add a `Settings` hashtable (`InheritGlobal`, `Patterns`, `Excludes`, `Feeds`, `Ignore`, `Strategy`,
`AllowPrerelease`, `Schedule`) and an `Expected` string; for example `patch-bump` is Patch with `0 3 * * *`, the
`major-*` repos are Major, `prerelease` allows pre-releases, `ignored-package` ignores `Tailspin.Legacy.*` and
`local-only` does not inherit and targets `AdventureWorks.*` only.

Get the API URL from the Aspire dashboard (the `api` resource's http endpoint) or `aspire describe`. After a manual
run, start the `scheduler` resource for a first pass, or use Run now (`POST /api/repositories/<id>/run`) on one repo.

## HeldRunSeeder

`AwaitingReview` is reached only when the AI loop repairs a compile break, which needs a model. The seeder fakes
that moment for the `held-review` demo repo: it creates a held run whose base commit is `held-review`'s real `main`
head and whose proposal files (a `Directory.Packages.props` manifest and a `src/App/Program.cs` agent edit) are the
real difference between `main` and `scenario/agent-fix`. Approve, Rebuild and Discard then run against the local
client and real verification. Retry reports "no model is configured".

It is an explicit-start Aspire resource named `held-run-seeder`, registered only when `LocalRepos__Enabled=true`.
Aspire injects the Azurite table and blob connection strings and the local-mode `AzureDevOps__*` settings, so there
is nothing to copy. It waits for `local-scenarios`, so the repos exist, and `local-scenarios-enroll` has already
enrolled `held-review`, so there is no manual generation or enrollment.

1. `$env:LocalRepos__Enabled='true'; $env:Model__Enabled='false'; aspire start`
2. Wait until `api` is `Running` and `local-scenarios-enroll` has finished.
3. Start the seeder: the Start button on `held-run-seeder` in the dashboard, or
   `aspire resource held-run-seeder start`. Read its output in the resource's console logs or with
   `aspire logs held-run-seeder`: it prints the run id and the `/runs/<id>` path. (If the repo is somehow not enrolled
   the seeder exits non-zero with a hint to enroll it.)

Starting it again while the repo has a held run prints the same run id and creates nothing. After Approve or Discard
the run is no longer held, so the next start seeds a new one. Start `scheduler` (or restart it) to see `SkippedHeld`
recorded for the held repo; to touch only that repo, first run `Set-LocalScenarioConfig.ps1 -Only held-review`.

Arguments (rarely needed): `--repo <name>` (default `held-review`) and `--root <path>` (default: `AzureDevOps__LocalRoot`,
else `<repo root>/.local/repos`). Running it outside Aspire with `dotnet run --project tools/local-scenarios/HeldRunSeeder`
needs `ConnectionStrings__tables` and `ConnectionStrings__blobs` set (use `127.0.0.1`, not `localhost`, for Azurite blob
endpoints); a missing one exits non-zero naming both.
