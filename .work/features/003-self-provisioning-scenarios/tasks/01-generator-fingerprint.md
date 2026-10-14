# 01 Generator fingerprint, skip/force modes and lock

**Status:** todo
**Feature:** 003-self-provisioning-scenarios
**Depends on:** none

## What to do
Teach the generator to know when its output is current, so later tasks can call it on every `aspire start` cheaply. All of this is PowerShell. There is one implementation, shared by the manual script and the AppHost.

1. In `LocalScenarios.psm1`, add and export the following:
   - `Get-ScenarioFingerprint -Root -ScriptsDirectory` returns the hash and its inputs. The hash is SHA-256, hex, over these parts in a fixed order:
     - a `$FingerprintVersion = 1` constant (bump it by hand when output-affecting behaviour changes outside the hashed files);
     - for each of `scenarios.psd1`, `LocalScenarios.psm1` and `New-LocalScenarios.ps1`: the file name plus its content read as UTF-8, with any BOM stripped and `\r\n` and `\r` replaced by `\n`;
     - the resolved absolute root, upper-casing the drive letter and lower-casing the whole path on Windows (`$IsWindows`);
     - the trimmed output of `dotnet --version`.
   - `Test-ScenarioOutputCurrent -Root -Catalogue -Fingerprint` returns `[pscustomobject]@{ Current = $bool; Reason = '<why not>' }`. It is current only if all of the following hold:
     - `<Root>/scenarios.fingerprint.json` exists, parses and its `fingerprint` equals the computed one;
     - the `.nupkg` file names in `<Root>/feed` exactly equal the catalogue's (reuse `Get-PackageFileName`);
     - every non-`NoFolder` repo folder exists and `git -C <repo> rev-parse --verify --quiet refs/heads/main` succeeds;
     - repos with `AgentFix` also have `refs/heads/scenario/agent-fix`;
     - no `NoFolder` repo folder exists;
     - `<Root>/prs` exists.

     Do not compare commit ids, because hand commits to `main` must not force a rebuild.
   - `Write-ScenarioFingerprint -Root -Fingerprint` writes `scenarios.fingerprint.json` (`fingerprint`, `fingerprintVersion`, `root`, `dotnetSdk`, `inputs` (file name and per-file hash), `generatedAtUtc`), UTF-8 without a BOM.
2. In `New-LocalScenarios.ps1`:
   - Add `[switch]$Force`.
   - Take an exclusive lock first: `[IO.File]::Open("<Root>/.generator.lock", OpenOrCreate, ReadWrite, FileShare.None)`, held in a `try/finally` for the whole run. If the open fails with an `IOException`, exit 3 with "Another local-scenarios generator is running against <Root> (lock <path>). Wait for it to finish." Create `<Root>` before opening the lock.
   - Unless `-Force` is set, compute the fingerprint and call `Test-ScenarioOutputCurrent`. If the output is current, print `Local scenarios are up to date (fingerprint <first 12>); skipping rebuild. Use -Force to rebuild.` and exit 0. Otherwise print `Rebuilding local scenarios: <Reason>`.
   - On rebuild, delete `scenarios.fingerprint.json` as the very first step, then run the existing delete/recreate, feed, repos and self-check steps. Write the fingerprint only after `Test-ScenarioFeed` passes.
   - Folder deletes retry up to 5 times with 1 s back-off. If they still fail, say which path is locked and suggest `aspire stop` or `dotnet build-server shutdown`.
   - Set `$ProgressPreference = 'SilentlyContinue'`. If `[Console]::IsOutputRedirected`, set `$PSStyle.OutputRendering = 'PlainText'`.
3. In `Invoke-Dotnet` (`LocalScenarios.psm1`), add `--disable-build-servers` to `build`, `restore` and `pack` calls, so a killed generator does not leave compiler server processes holding `.local` files. Check that the 85 s timing is not much worse; record it in the PR description.
4. Add `tools/local-scenarios/Test-LocalScenarioFingerprint.ps1`, a self-test with no Pester and no build. It copies the three input files into a temp scripts folder, imports the module from the real folder, and asserts:
   - the same inputs give the same hash;
   - converting the copied `scenarios.psd1` to CRLF, or to LF, gives the same hash;
   - adding a BOM gives the same hash;
   - changing one character in `scenarios.psd1` gives a different hash;
   - a different root gives a different hash;
   - the root `C:\x` equals `c:\X` on Windows only.

   Then it builds a fake root: empty-content files named after the catalogue's nupkgs, and `git init -b main` repos with one empty commit plus `scenario/agent-fix` where needed. It asserts that `Test-ScenarioOutputCurrent` is `Current` when the fingerprint file matches, and not current (with a sensible `Reason`) when the fingerprint file is missing, corrupt or mismatched, when a nupkg is missing or extra, when a repo folder is missing, when `scenario/agent-fix` is missing, and when a `NoFolder` repo exists. It prints `PASS`/`FAIL` per case and exits non-zero on any failure. It runs in a few seconds.

## Files
- `tools/local-scenarios/LocalScenarios.psm1`: fingerprint, current-check and write functions; `--disable-build-servers`
- `tools/local-scenarios/New-LocalScenarios.ps1`: `-Force`, lock, skip path, delete-fingerprint-first, retrying delete, plain output
- `tools/local-scenarios/Test-LocalScenarioFingerprint.ps1` (new): self-test

## Out of scope
- The fast reset and `-Reset`/`-ResetIfCurrent` (task 02).
- Anything in the AppHost or C# (tasks 03 to 05).
- README and docs updates (task 06). Do update the script's comment-based help.

## Done when
- [ ] `pwsh -NoProfile -File tools/local-scenarios/Test-LocalScenarioFingerprint.ps1` exits 0 with every case `PASS`.
- [ ] With `.local` deleted, `New-LocalScenarios.ps1` rebuilds and writes `.local/scenarios.fingerprint.json`. An immediate second run prints "up to date" and finishes in under 10 s (record the time).
- [ ] After whitespace is added to `scenarios.psd1` (and then reverted), the next run rebuilds.
- [ ] After `.local/repos/patch-bump` is deleted, the next run rebuilds with a reason that names it.
- [ ] `-Force` rebuilds even when the output is current.
- [ ] Killing the script (Ctrl+C) during the repo phase leaves no fingerprint file, and the next run rebuilds.
- [ ] Starting a second instance while one is running exits 3 with the lock message.
- [ ] A full rebuild still passes its self-checks, and its time is recorded in the PR description.

## Notes
- `.gitattributes` makes `*.ps1` CRLF on checkout. New `.ps1` files follow that.
- Keep pwsh 7 syntax (`#Requires -Version 7`). `$IsWindows` is available.
- `Test-ScenarioRepos` is internal to `New-ScenarioRepos`. Do not change its strict checks: they describe a freshly generated tree, not an intact one.
- `Get-PackageFileName` and `Get-RepoOption` already exist in the module; reuse them.
