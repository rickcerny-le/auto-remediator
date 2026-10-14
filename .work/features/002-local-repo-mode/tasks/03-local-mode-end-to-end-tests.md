# 03 Local-mode end-to-end runner tests

**Status:** done
**Feature:** 002-local-repo-mode
**Depends on:** 01, 02

## What to do
Add an automated test that drives a real `IRemediationRunner` resolved from `AddInfrastructure` in Local mode against temp git repos and a temp `TestFeed` folder. Everything is real except storage: the planner, aligner, feed resolver, verification (`dotnet restore`/`build`), `LocalRepositoryClient` and the unavailable agent. This catches integration problems early, before the generator and AppHost work: archive layout, rooted paths, push and PR record, lock files.

1. Build the host the way `InfrastructureExtensionsTests` does, with fake offline connection strings, plus `AzureDevOps:Mode=Local`, `AzureDevOps:LocalRoot=<temp>/repos` and `LocalPullRequestsRoot=<temp>/prs`. Replace the storage-backed services (`IManagedRepositoryStore`, `ITargetingSettingsStore`, `IRemediationRunStore`, `IChangeProposalStore`, `IVerificationLogStore`) with simple in-memory fakes using `services.Replace(...)`. Put the fakes in a reusable `InMemoryStores.cs`; the fakes inside `RemediationRunnerTests` are private and nested. Do not call `AddAgents`, so `UnavailableRemediationAgent` is used.
2. Fixture: a temp root, a `TestFeed` with ids unique to this test run, and git repos created with the CLI (root-level `README.md`, `core.autocrlf=false`, `net10.0` projects with no other package references, settings feeds = the temp feed path).
3. Cases:
   - **Patch bump, CPM.** The run ends `Completed`. `autoremediator/dependency-updates` has the new version in `Directory.Packages.props`. `main` and the working tree are unchanged. `prs/<repo>/1.json` exists, the run's PR URL equals its `url` and starts with `file:///`, and its `baseCommit`/`headCommit` equal `main`'s head and the update branch head.
   - **Idempotent re-run.** A second run on the same repo ends `Completed` with the same PR URL, and no `2.json` exists.
   - **NoUpdates.** The matched package is already at the policy target. The run ends `NoUpdates`, with no update branch and no PR folder.
   - **Restore rejection (NU).** The newer package version's nuspec depends on a package id that does not exist on the feed. The run ends `VerificationFailed`, the diagnostics include an `NU` code, `RemediationAttempts` is null (the loop was not entered), and there is no update branch.
   - **Collateral alignment.** Use nuspec ranges as in the FEATURE catalogue's Fabrikam.Data family, scaled down, with the Minor strategy. The updates contain one `Collateral` update flagged beyond policy, and the run is `Completed`.
   - **Per-csproj versions.** Two projects declare the package with `Version=`. Both files are edited on the branch.
   - **Compile break with no model (non-agentic).** The newer package version ships a `build/<id>.props` that adds a `Compile` item for a `.cs` file inside the package containing a deliberate type error (for example `int x = "s";`, giving CS0029). No real assembly is needed. The run ends `VerificationFailed`; the diagnostics include a `CS` code; `RemediationAttempts` is `0` (the loop was entered and the unavailable agent attempted nothing); the stored transcript contains "No model is configured"; there is no update branch and no PR folder. This pins the FEATURE.md "non-agentic" behaviour.
4. Self-skip when git or dotnet is unavailable (`Assert.Skip`), the same way the existing end-to-end verification tests do. Mark the class so it is easy to filter, for example `[Trait("Category", "LocalE2E")]`.

## Files
- `tests/AutoRemediator.Infrastructure.Tests/LocalModeEndToEndTests.cs` (new)
- `tests/AutoRemediator.Infrastructure.Tests/InMemoryStores.cs` (new)
- `tests/AutoRemediator.Infrastructure.Tests/TestFeed.cs`: extend (from task 02) so a package can carry extra files such as `build/<id>.props` and a source file

## Out of scope
- A CS break caused by a real API change in a package assembly. That needs real assemblies and is covered by the generator scenarios and the manual walkthrough (`major-breaking`). The injected-source case above is the cheap automated equivalent.
- Lock-file repos (covered by the `lock-file` scenario; add a case here only if cheap).
- Any production code change. If a case fails because of a real bug in tasks 01/02, fix it in those files and say so in the review notes. If it is caused by pre-existing behaviour (for example the PRD base-mismatch defect), assert the current behaviour and record it as a finding.

## Done when
- [ ] All seven cases exist and pass: `dotnet test tests/AutoRemediator.Infrastructure.Tests --filter "Category=LocalE2E"`.
- [ ] The tests leave no temp folders or git worktrees behind (cleanup in `Dispose`/`DisposeAsync`).
- [ ] Package ids are unique per run, and nothing is written under the repository's `.local/`.
- [ ] `dotnet test AutoRemediator.sln` passes (the class self-skips without git or dotnet).

## Notes
- Restore and build per case take seconds each. Keep the projects minimal (one `.cs` file).
- `RemediationRunRequested` needs `RunId`, `RepositoryId`, `Organization`, `Project`, `RepositoryName` and `RequestedAtUtc`. Take the repository from the in-memory store.
- `ITargetingSettingsStore.GetAsync` returns one global `TargetingSettings`. Set it per case.
- The no-model case relies on existing behaviour: `RemediationLoop` checks `agent.IsAvailable` first, writes "No model is configured, so no repair was attempted." and returns 0 attempts; `RemediationRunner` then records `Remediated(0, transcript)` and `VerificationFailed`. If the injected `.props` approach fails to produce a CS diagnostic, say so in the review notes and fall back to covering this case only in the walkthrough (task 10); do not change production code.
