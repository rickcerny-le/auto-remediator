# 10 Manual walkthrough doc and executed run

**Status:** done
**Feature:** 002-local-repo-mode
**Depends on:** 05, 07, 08, 09

## What to do
Write `docs/local-testing.md`, a step-by-step manual integration test of the whole system in local repository mode. Then execute it once end to end and record what actually happened.

1. **Doc contents:**
   - **Prerequisites:** .NET 10 SDK, Docker (for Azurite, the Service Bus emulator and, only if the model is enabled, Ollama), the Aspire CLI, PowerShell 7, and git on PATH.
   - **Generate:** `pwsh tools/local-scenarios/New-LocalScenarios.ps1`, what it creates, and that it purges the NuGet cache for its package ids.
   - **Start:** `$env:Model__Enabled='false'; $env:LocalRepos__Enabled='true'; aspire start`, then `aspire describe`, and how to find the API and Web URLs and the Azurite connection strings.
   - **Non-agentic mode:** with `Model__Enabled=false` there is no AI repair. The agent reports itself unavailable, the loop writes "No model is configured" with 0 attempts, and a build that fails after the bump ends `VerificationFailed` with no branch and no PR. The UI shows that status in amber (warning), not red. Mention that leaving the model enabled runs Ollama (`phi4`, a large first download) and makes compile-break repairs possible, but that this walkthrough's expected results assume the model is disabled.
   - **Enroll:** `Set-LocalScenarioConfig.ps1 -ApiUrl ... -Profile baseline`, with the Configuration page as the alternative.
   - **Trigger:** restart the `scheduler` resource (the exact `aspire` CLI command, verified, or the dashboard button). Optionally set `Scheduler__DevLoopEnabled`.
   - **Observe:**
     - the Runs page
     - run detail: updates table with matched, collateral and beyond-policy entries; verification outcome; diagnostics; log link; transcript link for `major-breaking`
     - the read-only "Pull request changes" panel (task 09) for `Completed` runs
     - the Dependency Map
     - `git -C .local/repos/<repo> log --oneline --all` and `git diff main..autoremediator/dependency-updates`
     - `.local/prs/<repo>/*.json`, including `baseCommit`/`headCommit`
   - **Expected-results matrix:** copied from FEATURE.md, one column per profile, with an "Actual" column filled in during step 2.
   - **Profiles:** switching profile; why to reset between profiles (regenerate, or delete the update branches and `.local/prs`) and the PRD "base mismatch" defect it avoids.
   - **Review gate:** seed with `HeldRunSeeder`, then Approve / Rebuild / Retry / Discard and the expected result of each; `SkippedHeld` on the next scheduler pass; re-generate to reset.
   - **Troubleshooting:**
     - `Skipped` with NU1301 means a feed path problem
     - NU1403 or the wrong API means a stale global cache: re-run the generator
     - "git was not found on PATH"
     - file:// links blocked by the browser: copy the link, or use the "Pull request changes" panel
     - the PR diff panel missing or failing for old runs after regeneration (their commits no longer exist)
     - uncommitted edits to a demo repo are invisible (archive of committed state)
     - restarting a resource vs `aspire stop`
     - Azurite run history surviving regeneration (stale commit ids in old runs)
2. **Execute** the walkthrough once for the `baseline` and `major` profiles, the `prerelease` profile for at least the `prerelease` repo, and the full review-gate section. Fill in the "Actual" column. Every divergence from "Expected" becomes a numbered finding at the end of the doc, with the run id and the observed status and diagnostics. Do not fix product behaviour in this task; Benny decides follow-ups.
3. Update docs:
   - README: link to `docs/local-testing.md` from the local-mode subsection (added in task 05).
   - `docs/PRD.md`: in §6.2, add one sentence saying a local repository mode (git binary, dev/test only) exists and is exempt from the no-clone rule. The "missing branch / unknown repository ends as `NoUpdates`" Known-defects entry is already in the PRD (user decision); do not add it again. If step 2 shows it behaving differently from that entry, record a finding instead of editing it.

## Files
- `docs/local-testing.md` (new)
- `README.md`: link
- `docs/PRD.md`: §6.2 note only

## Out of scope
- Fixing any finding.
- Playwright automation of the walkthrough (task 09 automates only the PR diff panel checks).

## Done when
- [ ] `docs/local-testing.md` covers every bullet in step 1, and every command in it was run as written on this machine.
- [ ] The matrix has an "Actual" value for every repo under `baseline` and `major`, and for `prerelease` under the `prerelease` profile.
- [ ] Non-agentic check, recorded with its run id: with `Model__Enabled=false` under `major`, `major-breaking` ends `VerificationFailed` and shows:
  - an amber (warning) status pill
  - a `CS1061` diagnostic
  - remediation with 0 attempts and a transcript containing "No model is configured"
  - no `autoremediator/dependency-updates` branch in `.local/repos/major-breaking`, no `.local/prs/major-breaking` folder, and no PR link
- [ ] For at least one `Completed` run, the "Pull request changes" panel lists the same files as `git diff --name-only main..autoremediator/dependency-updates` in that repo.
- [ ] The review-gate section records the actual outcome of Approve, Rebuild, Retry and Discard, and of the `SkippedHeld` pass.
- [ ] Findings (if any) are listed with run ids, or the doc states "no divergences".
- [ ] README links the doc, and the only PRD change is the §6.2 sentence.
- [ ] `dotnet test AutoRemediator.sln` still passes (no code changes expected).

## Notes
- Leave `restore-conflict` (NU1102 vs NU1605), `family-alignment` under `patch`, and the `missing-branch`/`does-not-exist` outcomes as "expected (to confirm)" in the matrix until observed.
- One scheduler pass over all repos takes several minutes; use `-Only` to rerun a subset.
- `aspire logs remediation` is the fastest way to see per-run progress.
- Likely finding: the run detail's "AI remediation" section text ("…handed to an AI agent, which proposed source edits…") reads as if an agent acted, even for a 0-attempt "no model" run. Record what the page says; don't change it here.
