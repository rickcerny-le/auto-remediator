# 07 Scenario generator: demo repos and enrollment profiles

**Status:** done
**Feature:** 002-local-repo-mode
**Depends on:** 05, 06 (05 only for the live enrollment/dependency-map checks)

## What to do
Implement `New-ScenarioRepos` so that `New-LocalScenarios.ps1` also creates every demo repo in the FEATURE catalogue under `<Root>/repos`. Then add a script that enrolls them and applies a settings profile through the running API.

1. **Repos.** Add the repo definitions to `scenarios.psd1`: name, layout (CPM or per-csproj), projects, declared packages and versions, and extras. For each repo:
   - Write files as UTF-8 without a BOM with LF line endings: a root `README.md` describing the scenario and its expected outcome per profile, and `.gitignore` (`bin/`, `obj/`).
   - CPM repos get `Directory.Packages.props` with `ManagePackageVersionsCentrally`. Per-csproj repos put `Version=` on each `PackageReference`.
   - Projects target `net10.0`. `Program.cs` actually uses an API from every referenced package, so a breaking change really breaks the build.
   - `NuGet.config` gets `<clear/>` and one source whose value is the **absolute** `<Root>/feed` path, resolved at generation time.
   - `git init -b main`, `git config core.autocrlf false`, then one commit with a fixed identity and fixed `GIT_AUTHOR_DATE`/`GIT_COMMITTER_DATE`, and `-c commit.gpgsign=false`.
   - Leave `main` checked out.
   - Repo-specific extras:
     - `minor-bump`: two projects (`src/Api`, `src/Worker`) plus a root `.sln`/`.slnx`, both projects declaring `Contoso.Http 2.1.0`.
     - `lock-file`: `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`, **without** `RestoreLockedMode`. Run `dotnet restore` before the commit so `packages.lock.json` is committed.
     - `held-review`: after the `main` commit, create branch `scenario/agent-fix` with one commit that bumps `Northwind.Messaging` to `4.0.0` in `Directory.Packages.props` and rewrites the `Publish` call to `PublishAsync(new Message(...))`. This is the change the AI would have proposed. Then switch back to `main`.
     - `missing-branch`: a normal repo with only `main`. It is enrolled with target `develop` in step 2.
     - `disabled-repo`: same shape as `patch-bump`.
     - `does-not-exist`: no folder at all.
   - Self-check: `dotnet build` each repo at `main` and require success (this proves the feed and configs). Then `git clean -xdf` so no `bin/`/`obj/` remain. Do not commit build output.
2. **Enrollment and settings.** `tools/local-scenarios/Set-LocalScenarioConfig.ps1 -ApiUrl <http://localhost:port> [-Profile baseline|patch|major|prerelease] [-Only <names>] [-Root <path>]`.
   - Idempotent: `GET /api/repositories`, match by `Name`, then `PUT` an existing entry or `POST` a missing one.
   - Organization `local`, project `scenarios`, target branch `main`, except `missing-branch` → `develop`. `Enabled` is true except `disabled-repo`.
   - `-Only` enables just the named repos and disables the rest, without deleting them.
   - Then `PUT /api/settings` with the profile:
     - patterns `Contoso.*`, `Fabrikam.*`, `Northwind.*`, `Tailspin.*`
     - excludes empty
     - feeds = absolute `<Root>/feed`
     - ignore `Tailspin.Legacy.*`
     - strategy and allowPrerelease: `baseline` Minor/false, `patch` Patch/false, `major` Major/false, `prerelease` Minor/true
   - Print a table of what was enrolled and the active profile, then the next step: restart the `scheduler` resource.
3. Document both scripts in a short `tools/local-scenarios/README.md` (usage only; the full walkthrough is task 10).

## Files
- `tools/local-scenarios/scenarios.psd1`: repo definitions
- `tools/local-scenarios/LocalScenarios.psm1`: `New-ScenarioRepos`
- `tools/local-scenarios/Set-LocalScenarioConfig.ps1` (new)
- `tools/local-scenarios/README.md` (new)

## Out of scope
- Seeding the held run (task 08).
- Triggering runs. The scheduler restart is manual, as there is no "Run now" endpoint (PRD §6.12).
- Changes to the API or UI.

## Done when
- [ ] `pwsh tools/local-scenarios/New-LocalScenarios.ps1` creates every repo in the catalogue except `does-not-exist`. Each is a git repo on `main` with one commit and a clean working tree (`git status --porcelain` empty), and the script exits 0.
- [ ] Every generated repo builds at `main` (the script's self-check), and `held-review`'s `scenario/agent-fix` branch also builds.
- [ ] `lock-file` has a committed `packages.lock.json` that pins `Contoso.Logging` 1.2.0.
- [ ] Every generated `NuGet.config` contains the absolute feed path for the `-Root` used. The scripts contain no hard-coded user path.
- [ ] Two consecutive generations give identical `main` commit ids for every repo except `lock-file` (its lock-file content hash may differ with re-packed nupkgs). This shows fixed dates and identity are in effect.
- [ ] Against a running app (task 05 local mode), `Set-LocalScenarioConfig.ps1` run twice leaves exactly one entry per scenario name in `GET /api/repositories`. `GET /api/settings` matches the requested profile. The Configuration page shows the repos and settings.
- [ ] The Dependency Map page shows matched packages per repo with the statuses implied by the active profile: for example, under `baseline`, `ignored-package`'s `Tailspin.Legacy.Data` is Ignored, `up-to-date` is UpToDate, and `prerelease` is Outdated → 1.0.1.

## Notes
- `Invoke-RestMethod` with `-ContentType application/json` and `ConvertTo-Json -Depth 5`. The DTO shapes are in `src/AutoRemediator.Contracts/Dtos/ConfigurationDtos.cs`; strategy is a string (`"Minor"`).
- The API URL comes from the Aspire dashboard (the `api` resource's http endpoint) or `aspire describe`.
- Every repo needs a root-level file. The wrapper-folder heuristic in `VerificationWorkspaceFactory` would otherwise strip a lone top-level folder.
- `dotnet build` in the self-check populates the global packages cache with the fresh packages. That is fine because task 06 purged the stale ones first.
