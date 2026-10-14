# 08 Held-for-review scenario seeder

**Status:** done
**Feature:** 002-local-repo-mode
**Depends on:** 01, 05, 07

## What to do
`AwaitingReview` is reached only when the AI loop repairs a compile break, and the model is out of play. Add a small console tool that seeds a realistic held run for the `held-review` demo repo, the same way `ReviewGateTests.SeedHeldRunAsync` seeds one. The difference is that this proposal is real: its base commit is `held-review`'s actual `main` head, and its files come from the repo's `scenario/agent-fix` branch. Approve, Rebuild and Discard then exercise the local client and real verification.

1. Create a console project `tools/local-scenarios/HeldRunSeeder/HeldRunSeeder.csproj` (`net10.0`) that references `AutoRemediator.Infrastructure` (and through it `Domain`). Add it to `AutoRemediator.sln` under a `tools` solution folder so CI builds it.
2. Behaviour (`dotnet run --project tools/local-scenarios/HeldRunSeeder -- [--root <path>] [--repo held-review]`):
   - Build a host like `ReviewGateTests.BuildStorageProvider`:
     - `ConnectionStrings:tables` and `ConnectionStrings:blobs` come from configuration (environment variables), injected by Aspire when the seeder runs as the explicit-start `held-run-seeder` resource (user decision: no manual copying). A clear error names both variables if either is missing (no silent `UseDevelopmentStorage=true` default, because Aspire's Azurite ports differ)
     - `servicebus` gets the same fake value the tests use
     - `AzureDevOps:Mode=Local`, with `LocalRoot` set to `--root` (default `<repo root>/.local/repos`, found by walking up from the executable to the folder containing `AutoRemediator.sln`)
   - Find the enrolled repository with `Name == --repo` via `IManagedRepositoryStore`. If it is missing, exit non-zero with "run Set-LocalScenarioConfig.ps1 first".
   - If `IRemediationRunStore.FindAwaitingReviewAsync` already returns a held run for it, print that run id and exit 0 (idempotent).
   - Get the base commit from `IAzureDevOpsClient.GetBranchHeadAsync(repo, repo.TargetBranch)`. Get the changed files by running `git diff --name-only main scenario/agent-fix` and `git show <ref>:<path>` for before and after (the git CLI is fine here). Classify `Directory.Packages.props`/`*.csproj`/`*.props` as `Manifest` and everything else as `AgentEdit`. Use rooted paths (`/src/...`).
   - Create:
     - a `RemediationRun` with updates `[Northwind.Messaging 3.2.0 → 4.0.0]`
     - a canned transcript, stored with `IVerificationLogStore.StoreAsync(runId, text, "remediation-transcript.md")`, that states it was seeded for local testing
     - `run.Remediated(1, transcriptRef)`
     - a `ChangeProposal` with diagnostics `[CS1061 'MessageBus' does not contain a definition for 'Publish' ... at src/.../Program.cs]`, attempts 1, verifiedAt now
     - `proposals.StoreAsync`, then `run.AwaitingReview(reference, now)` and `runStore.SaveAsync`

     Follow the method order `RemediationRunner` uses so the run's state transitions are legal (check `RemediationRun`'s guards).
   - Print the run id and the Web URL path `/runs/<id>`.
3. AppHost: when `LocalRepos:Enabled` is true, register the seeder as `builder.AddProject<Projects.HeldRunSeeder>("held-run-seeder")` with `WithReference(tables)`, `WithReference(blobs)`, `WaitFor(storage)`, `WithExplicitStart()` and the existing `WithLocalRepos` helper (so it gets `AzureDevOps__Mode/LocalRoot/LocalPullRequestsRoot`). Add the project reference to the AppHost csproj. It runs only on demand (dashboard Start button or `aspire resource held-run-seeder start`).
4. Add usage to `tools/local-scenarios/README.md`.

## Files
- `tools/local-scenarios/HeldRunSeeder/HeldRunSeeder.csproj` (new)
- `tools/local-scenarios/HeldRunSeeder/Program.cs` (new)
- `AutoRemediator.sln`: add the project under a `tools` folder
- `src/AutoRemediator.AppHost/AppHost.cs` and `AutoRemediator.AppHost.csproj`: the explicit-start resource (local mode only)
- `tools/local-scenarios/README.md`: usage

## Out of scope
- An API endpoint for seeding. (The open question about how the seeder gets its connection strings is settled: an explicit-start Aspire resource in local mode, per the user.)
- Making a real run reach `AwaitingReview` (would need a working or fake model).

## Done when
- [ ] `dotnet build AutoRemediator.sln` builds the seeder, and `dotnet test AutoRemediator.sln` still passes.
- [ ] With the app running in local mode (task 05) and the scenarios enrolled (task 07), starting the `held-run-seeder` resource prints a run id. The run appears in Home's "Held for review" panel, and its detail page shows the review panel with a manifest file and an agent-edit file whose before/after match `main` and `scenario/agent-fix`.
- [ ] Running the seeder again prints the same run id and creates nothing new.
- [ ] Restarting `scheduler` records a `SkippedHeld` run for `held-review`.
- [ ] Approve: the run becomes `Completed`. `git -C .local/repos/held-review diff main..autoremediator/dependency-updates` shows the bump and the `PublishAsync` edit. `.local/prs/held-review/1.json` exists with `baseCommit`/`headCommit` set, and the run's PR link equals its `url`.
- [ ] Seeding again after Approve creates a new held run, because the previous one is no longer `AwaitingReview`. On it: Retry reports "no model is configured, so retry could not run" and the run stays held. Rebuild re-verifies and stays held with a replaced proposal. Discard ends `Discarded`, and the update branch does not move.
- [ ] A missing connection string (seeder run outside Aspire) or an unenrolled repo exits non-zero with a message saying what to do.

## Notes
- Reference: `tests/AutoRemediator.Web.FunctionalTests/ReviewGateTests.cs` (`BuildStorageProvider`, `SeedHeldRunAsync`, `NewProposal`), and `src/AutoRemediator.Infrastructure/Remediation/RemediationRunner.cs` (the held branch).
- `ChangeProposal` lives in `src/AutoRemediator.Domain/ChangeProposal.cs`, and `ProposedFileOrigin` has `Manifest`, `LockFile` and `AgentEdit`.
- `InternalsVisibleTo` does not cover the seeder. Use only public types (`AddInfrastructure`, the store interfaces, `IAzureDevOpsClient`, Domain types), all of which are public today.
