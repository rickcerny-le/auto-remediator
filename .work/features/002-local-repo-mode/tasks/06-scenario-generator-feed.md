# 06 Scenario generator: feed packages

**Status:** done
**Feature:** 002-local-repo-mode
**Depends on:** none (independent of 01–05; order is for review flow)

## What to do
Create the committed generator entry point and its feed half. One command rebuilds `.local/feed` with exactly the package catalogue in FEATURE.md. Task 07 adds the repos to the same entry point.

1. `tools/local-scenarios/New-LocalScenarios.ps1` (`#Requires -Version 7`, `$ErrorActionPreference='Stop'`, `Set-StrictMode -Version Latest`).
   - Parameter `-Root`, defaulting to `<repo root>/.local` computed from `$PSScriptRoot`. Never hard-code a user path.
   - It deletes and recreates `<Root>/feed`, `<Root>/repos` and `<Root>/prs`. Removing the old spike content is intended.
   - It calls `New-ScenarioFeed` (this task) and then `New-ScenarioRepos` (task 07; a stub that does nothing is fine until then).
   - Keep functions in `tools/local-scenarios/LocalScenarios.psm1`, or dot-sourced `.ps1` files. Keep the scenario data in `tools/local-scenarios/scenarios.psd1`: package ids, versions, dependency ranges and API variant, so the catalogue is reviewable as data rather than buried in code.
2. **Packing.** For each package version, generate a throwaway `netstandard2.0` library project in a temp work folder outside the repo, then `dotnet pack -c Release -p:PackageVersion=<v> -o <Root>/feed`.
   - Source per API variant is a small here-string. Each package exposes a type whose API stays stable across versions unless the catalogue says otherwise. Also include `public const string Version = "<v>"` so a consumer can show which version it compiled against.
   - `Northwind.Messaging 3.2.0`: `MessageBus.Publish(string topic, string payload)`. `4.0.0`: no `Publish`; `Task PublishAsync(Message message)` plus a `Message` record.
   - `Northwind.Serialization 2.0.0`: additive only.
   - **Dependency ranges.** Declare them as `PackageReference` with the exact range string (for example `[2.0.0,3.0.0)`). Pack in dependency order against a temp `NuGet.config` (`<clear/>` + the output feed only), so ranges restore from what was already packed.
   - `Fabrikam.Reporting 1.1.0` depends on `Fabrikam.Data.Abstractions [3.0.0,4.0.0)`, which deliberately never exists. Produce it with a hand-written `.nuspec` (`dotnet pack` with `NuspecFile`, or a zip built in PowerShell). Do not publish a 3.x Abstractions.
   - Pass `-p:NoWarn=NU5104%3BNU5128%3BCS1591`, or equivalent, so packing is quiet. Fail the script on any non-zero exit code.
3. **Cache purge.** Before packing, delete the global-packages folder for every generated id, lower-cased: `$env:NUGET_PACKAGES` if set, otherwise the path from `dotnet nuget locals global-packages --list`. Also delete `contoso.core` (the old spike id).
4. **Self-check at the end.** List `<Root>/feed` and compare it with the catalogue: exact file names, no extras. Read the `.nuspec` from each nupkg that declares dependencies and assert the range strings. Print a one-line summary (package count and elapsed time) and exit non-zero on any mismatch.
5. `.gitignore`: add `.local/` with a comment naming the generator.

## Files
- `tools/local-scenarios/New-LocalScenarios.ps1` (new)
- `tools/local-scenarios/LocalScenarios.psm1` (new; or equivalent dot-sourced files)
- `tools/local-scenarios/scenarios.psd1` (new)
- `.gitignore`: `.local/`

## Out of scope
- Demo repos, enrollment and seeding (tasks 07, 08).
- Signing packages or byte-for-byte reproducible nupkgs. The deterministic part is the catalogue: same ids, versions, APIs and ranges every run.

## Done when
- [ ] `pwsh tools/local-scenarios/New-LocalScenarios.ps1` run from any working directory creates `.local/feed` with exactly the nupkgs in the FEATURE catalogue (29 files) and exits 0. Running it a second time gives the same file list.
- [ ] The self-check verifies these ranges and fails if any is wrong:
  - `Fabrikam.Data 1.0.0` → Abstractions `[1.0.0, 2.0.0)`
  - `Fabrikam.Data 1.2.0` → Abstractions `[2.0.0, 3.0.0)`
  - `Fabrikam.Data.SqlServer 1.0.0` → Data `[1.0.0, 2.0.0)`
  - `Fabrikam.Data.SqlServer 1.2.0` → Data `[1.2.0, 2.0.0)`
  - `Fabrikam.Reporting 1.0.0` → Abstractions `[1.0.0, 2.0.0)`
  - `Fabrikam.Reporting 1.1.0` → Abstractions `[3.0.0, 4.0.0)`
- [ ] `Northwind.Messaging.4.0.0.nupkg`'s assembly has no `Publish` method (check with a tiny consumer build in the self-check, or by inspection documented in the PR notes).
- [ ] After a run, `~/.nuget/packages` (or `NUGET_PACKAGES`) contains no folder for any generated id that predates the run.
- [ ] `git status` shows no files under `.local/`, and nothing is written outside `-Root` except the temp work folder, which is removed at the end.
- [ ] The script contains no absolute user path. Checked with `grep -i "users\\\\" tools/local-scenarios` returning nothing.

## Notes
- Count: Logging 3 + Http 5 + Serialization 2 + Messaging 2 + Tailspin.Core 2 + Tailspin.Legacy.Data 2 + Telemetry 3 + Abstractions 2 + Data 2 + SqlServer 2 + Reporting 2 + AdventureWorks.Text 2 = 29. If the catalogue changes, update the number in "Done when" and in FEATURE.md together.
- Do not name a committed folder `packages`: `.gitignore` has `**/[Pp]ackages/*`. `*.nupkg` and `nuget.config` are also ignored, which is fine because everything generated lives under `.local/`.
- pwsh 7 `Set-Content`/`Out-File` default to UTF-8 without a BOM, which is why Windows PowerShell 5.1 is excluded.
- Prior art: `git show feat/local-remediation:scripts/new-local-fixture.ps1`.
- Packing about 29 versions takes a few minutes. Reuse one project folder per package id across its versions to save restore time.
