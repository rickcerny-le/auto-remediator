# 10 Local scenarios with per-repo settings

**Status:** todo
**Feature:** 004-per-repo-settings
**Depends on:** 06

## What to do
Make each local scenario repo carry its own settings, so one pass reproduces every scenario. Remove the four global profiles and the profile-switch routine, but keep the 003 fingerprint, provisioning, reset and enrollment machinery.

1. **Catalogue** (`tools/local-scenarios/scenarios.psd1`):
   - Replace each repo's `Outcomes` hashtable with a single `Expected` string, and add an optional `Settings` hashtable with the keys `InheritGlobal`, `Patterns`, `Excludes`, `Feeds`, `Ignore`, `Strategy`, `AllowPrerelease` and `Schedule`. The literal `{feed}` in `Feeds` is replaced with the absolute local feed path at enrollment.
   - Add a top-level `GlobalSettings` hashtable: patterns `Contoso.*`, `Fabrikam.*`, `Northwind.*`, `Tailspin.*`; feeds `{feed}`; no excludes; no ignore; strategy `Minor`; `AllowPrerelease` `$false`; no default schedule (so the built-in daily `0 0 * * *` applies).
   - Per repo:

     | Repo | Settings | Expected |
     | --- | --- | --- |
     | patch-bump | Strategy Patch, Schedule `0 3 * * *` | Contoso.Logging 1.2.0 -> 1.2.3, Completed |
     | minor-bump | inherits | Contoso.Http -> 2.4.1, both csproj edited, Completed |
     | major-compiles | Strategy Major | Northwind.Serialization -> 2.0.0, Completed |
     | major-breaking | Strategy Major | VerificationFailed, CS1061, 0 attempts, "No model is configured" transcript, no branch |
     | up-to-date | inherits | NoUpdates; map UpToDate |
     | no-matches | inherits | NoUpdates; nothing matched (AdventureWorks is not in the global patterns) |
     | **local-only** (new) | InheritGlobal false, Patterns `AdventureWorks.*`, Feeds `{feed}` | AdventureWorks.Text 1.0.0 -> 1.5.0, Completed; no Contoso/Fabrikam/Northwind/Tailspin package is considered |
     | ignored-package | Ignore `Tailspin.Legacy.*` | Tailspin.Core -> 1.0.2 only; Tailspin.Legacy.Data Ignored |
     | prerelease | AllowPrerelease true | Fabrikam.Telemetry -> 1.1.0-beta.2 |
     | family-alignment | inherits | SqlServer and Data -> 1.2.0; Abstractions -> 2.0.0 collateral beyond policy; Completed |
     | restore-conflict | inherits | Reporting -> 1.1.0; VerificationFailed with NU diagnostic; loop not entered |
     | lock-file | inherits | -> 1.2.3 with the regenerated lock file |
     | held-review | inherits | Seeded AwaitingReview; Run now refused (409); a due scheduler pass records SkippedHeld |
     | missing-branch | inherits (TargetBranch develop) | NoUpdates (known gap) |
     | does-not-exist | inherits (NoFolder) | NoUpdates (known gap) |
     | disabled-repo | inherits (Enabled false) | No scheduled run; Run now runs it (Contoso.Logging 1.2.0 -> 1.2.3, Completed, Manual) and it stays disabled |

   - `local-only` declares `AdventureWorks.Text` 1.0.0 and also `Contoso.Logging` 1.2.0. The Contoso package proves that the global patterns are **not** inherited: it must stay at 1.2.0 and must not appear in that repo's map entries.
2. **Generator** (`LocalScenarios.psm1`, `Write-ScenarioRepoFiles`): replace the per-profile README table with an "Expected outcome" line and a "Settings" section listing the repo's settings, or "Inherits the global baseline". Keep everything else, so the 003 fingerprint changes only because these files changed (one rebuild, as intended).
3. **Enrollment** (`Set-LocalScenarioConfig.ps1`):
   - Remove `-Profile` and the `$profiles` table.
   - Build the global body from `GlobalSettings` (with `{feed}` substituted) and `PUT /api/settings`.
   - For each repo, send `settings` (a `RepositorySettingsDto` shape: missing lists become `@()`, missing scalars `$null`, `inheritGlobal` defaulting to `$true`) on both POST and PUT, so re-enrolling resets each repo to the catalogue.
   - Keep `-Only`.
   - Print a table with Name, Enabled, Target, Settings summary and Action.
   - Replace the closing "next step" lines:
     - Under the wrapper: "the first scheduler pass after start runs every enabled repo; use Run now for a single repo".
     - Manual: "start the scheduler for a first pass, or use Run now on a repo".
4. **Wrapper** (`LocalScenarioSetup`):
   - Remove `--profile` from `Options` (it becomes "unknown argument" for enroll).
   - Remove `Enrollment.Profiles` and `Enrollment.ValidateProfile`.
   - Remove the profile argument from `ScriptCommand.ForEnroll` and the "enrolling profile" log line (it becomes "enrolling scenario repos").
   - Update `tests/AutoRemediator.Tools.Tests/SetupTests.cs` to match: delete the profile tests, and adjust the enroll parse and command tests.
5. **AppHost** (`AppHost.cs`):
   - Stop reading `LocalRepos:Profile` as a setting and stop passing `--profile`.
   - If `LocalRepos:Profile` is set (non-blank), print once at startup: `AppHost: LocalRepos__Profile is obsolete and ignored: each scenario repo carries its own settings (see docs/local-testing.md).`
   - Remove the profile from the startup summary line.
   - Update the comments that mention profiles, including "the fast reset between profiles" on `local-scenarios-reset` (it becomes "between runs").
   - **Tick passthrough.** If `Scheduler:DevLoopEnabled` or `Scheduler:DevLoopIntervalSeconds` is set in the AppHost's configuration (env vars `Scheduler__DevLoopEnabled` and `Scheduler__DevLoopIntervalSeconds` before `aspire start`), forward them to the `scheduler` resource with `WithEnvironment`. Locally the scheduler can then act as a real tick, with no code edits, so due/not-due behaviour can be observed. When they are unset, nothing changes. Include them in the startup line when set.
6. **Self-test.** Keep `Test-LocalScenarioFingerprint.ps1` passing. If it asserts on the README profile table or `Outcomes`, update those assertions.

## Files
- `tools/local-scenarios/scenarios.psd1`, `LocalScenarios.psm1`, `Set-LocalScenarioConfig.ps1`, `Test-LocalScenarioFingerprint.ps1` (if needed)
- `tools/local-scenarios/LocalScenarioSetup/Options.cs`, `Enrollment.cs`, `ScriptCommand.cs`, `Program.cs`
- `tests/AutoRemediator.Tools.Tests/SetupTests.cs`
- `src/AutoRemediator.AppHost/AppHost.cs`

## Out of scope
- Docs (task 11). Leave `docs/local-testing.md` and the READMEs for task 11, even where they now mention profiles.
- `HeldRunSeeder` (unchanged).
- Any change to fingerprint inputs or the reset semantics.

## Done when
- [ ] `pwsh -NoProfile -File tools/local-scenarios/Test-LocalScenarioFingerprint.ps1` exits 0.
- [ ] `dotnet test tests/AutoRemediator.Tools.Tests` passes, and no test or source references `Profile`/`--profile` except the AppHost deprecation message.
- [ ] `pwsh tools/local-scenarios/New-LocalScenarios.ps1 -Force` builds all repos, including `local-only`, and its self-checks pass.
- [ ] With the app running in local mode, `local-scenarios-enroll` finishes 0, and `GET /api/repositories` shows the catalogue's settings on every repo (for example `local-only` with `inheritGlobal: false`). `GET /api/settings` shows the global baseline with the absolute feed path, no ignore list, and Minor.
- [ ] Re-running `Set-LocalScenarioConfig.ps1 -ApiUrl <url>` by hand after editing a repo's settings in the UI resets them to the catalogue.
- [ ] `$env:LocalRepos__Profile='major'; aspire start` prints the deprecation line, and enrollment still uses the per-repo catalogue.
- [ ] With `$env:Scheduler__DevLoopEnabled='true'; $env:Scheduler__DevLoopIntervalSeconds='60'` before `aspire start`, the `scheduler` resource's environment (dashboard) contains both values, and the scheduler keeps running instead of exiting after one pass.
- [ ] `dotnet build AutoRemediator.sln` passes.

## Notes
- PowerShell `ConvertTo-Json` turns `@()` into `[]` only when the depth is sufficient and the value is not unwrapped. Use `-InputObject` (as `Invoke-Api` already does) and `[string[]]@()` for empty lists, then check the body against the API with one repo first.
- `scenarios.psd1` is hashed by the fingerprint, so the first start after this task rebuilds `.local` (~85 s). That is expected; say so in the review notes.
- The 002/003 decision "scenarios run under global settings profiles" is superseded. Benny logs that.
