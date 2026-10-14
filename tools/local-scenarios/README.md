# Local scenarios

PowerShell 7 tooling that builds realistic upgrade scenarios on disk for local repository mode. The full
walkthrough lives in the feature docs; this file is usage only. Windows PowerShell 5.1 is not supported.

## New-LocalScenarios.ps1

```powershell
pwsh tools/local-scenarios/New-LocalScenarios.ps1 [-Root <path>]
```

Deletes and recreates `<Root>/feed`, `<Root>/repos` and `<Root>/prs` (default root: `<repo root>/.local`, which is
gitignored). It packs the NuGet folder feed, then creates every demo repo in `scenarios.psd1` as a git repo on `main`
with one commit (fixed identity and dates, so commit ids are reproducible; `lock-file` differs because its lock file
hashes re-packed nupkgs). Each repo's `NuGet.config` points at the absolute `<Root>/feed` path. The script builds
every repo (and `held-review`'s `scenario/agent-fix` branch) as a self-check, so it needs the .NET 10 SDK and `git`.
It exits non-zero if any check fails. It also purges the generated package ids from the NuGet global packages cache.

Each repo's `README.md` states the scenario and its expected outcome per settings profile.

## Set-LocalScenarioConfig.ps1

```powershell
pwsh tools/local-scenarios/Set-LocalScenarioConfig.ps1 -ApiUrl http://localhost:<port> `
    [-Profile baseline|patch|major|prerelease] [-Only <name>,<name>] [-Root <path>]
```

Enrolls every scenario repo (organization `local`, project `scenarios`, target `main`; `missing-branch` targets
`develop`, `disabled-repo` is disabled) and applies the settings profile. Safe to run repeatedly: repos are matched by
name and updated, never duplicated. `-Only` enables just the named repos and disables the rest.

| Profile | Strategy | Allow pre-release |
| --- | --- | --- |
| baseline (default) | Minor | no |
| patch | Patch | no |
| major | Major | no |
| prerelease | Minor | yes |

All profiles use patterns `Contoso.*`, `Fabrikam.*`, `Northwind.*`, `Tailspin.*`, ignore `Tailspin.Legacy.*`, and the
absolute `<Root>/feed` path. Settings are global, so a profile applies to every repo.

Get the API URL from the Aspire dashboard (the `api` resource's http endpoint) or `aspire describe`. Start the app with
`LocalRepos__Enabled=true` (and `Model__Enabled=false` for no model). After the script runs, restart the `scheduler`
resource to start a pass over the enabled repos.

## HeldRunSeeder

`AwaitingReview` is reached only when the AI loop repairs a compile break, which needs a model. The seeder fakes
that moment for the `held-review` demo repo: it creates a held run whose base commit is `held-review`'s real `main`
head and whose proposal files (a `Directory.Packages.props` manifest and a `src/App/Program.cs` agent edit) are the
real difference between `main` and `scenario/agent-fix`. Approve, Rebuild and Discard then run against the local
client and real verification. Retry reports "no model is configured".

It is an explicit-start Aspire resource named `held-run-seeder`, registered only when `LocalRepos__Enabled=true`.
Aspire injects the Azurite table and blob connection strings and the local-mode `AzureDevOps__*` settings, so there
is nothing to copy.

1. `$env:LocalRepos__Enabled='true'; $env:Model__Enabled='false'; aspire start`
2. `pwsh tools/local-scenarios/Set-LocalScenarioConfig.ps1 -ApiUrl http://localhost:<api port> -Only held-review`
   (enrolls the repo; the seeder exits non-zero with that hint if it is missing)
3. Start the seeder: the Start button on `held-run-seeder` in the dashboard, or
   `aspire resource held-run-seeder start`. Read its output in the resource's console logs or with
   `aspire logs held-run-seeder`: it prints the run id and the `/runs/<id>` path.

Starting it again while the repo has a held run prints the same run id and creates nothing. After Approve or Discard
the run is no longer held, so the next start seeds a new one. Restart `scheduler` to see `SkippedHeld` recorded for
the held repo.

Arguments (rarely needed): `--repo <name>` (default `held-review`) and `--root <path>` (default: `AzureDevOps__LocalRoot`,
else `<repo root>/.local/repos`). Running it outside Aspire with `dotnet run --project tools/local-scenarios/HeldRunSeeder`
needs `ConnectionStrings__tables` and `ConnectionStrings__blobs` set (use `127.0.0.1`, not `localhost`, for Azurite blob
endpoints); a missing one exits non-zero naming both.
