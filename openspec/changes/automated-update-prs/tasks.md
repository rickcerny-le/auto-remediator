## 1. Domain

- [ ] 1.1 Add a `RunStatus` enum (Reading, Analyzing, Applying, CreatingPr, Completed, NoUpdates, Failed) and a `DependencyUpdate` record (`PackageId`, `FromVersion`, `ToVersion`)
- [ ] 1.2 Add a `RemediationRun` entity (id, repositoryId, repositorySlug, status, startedAtUtc, finishedAtUtc?, updates[], pullRequestUrl?, error?) with transitions to the terminal statuses
- [ ] 1.3 Add a `ManifestEditor` (pure) that sets a package's version in `Directory.Packages.props`/`*.csproj` text via a targeted replace, leaving all other content unchanged; returns unchanged content when the package/version is absent

## 2. Update planning (reuse Slice-1 analysis)

- [ ] 2.1 Factor the per-repo analysis (read → parse → match → resolve latest → status) so it is shared by the dependency map and update planning
- [ ] 2.2 Add `IUpdatePlanner` (+ impl) producing a `RepositoryUpdatePlan { updates: [{packageId, from, to}], changedManifests: [{path, newContent}] }` for a repo — only matched, outdated, concrete-version packages; empty plan when nothing applies

## 3. Azure DevOps write operations

- [ ] 3.1 Extend `IAzureDevOpsClient` with `GetBranchHeadAsync(repo, branch)` → latest commit id or null
- [ ] 3.2 Add `PushFilesAsync(repo, branch, baseCommitId, changes, message)` — REST `pushes` creating the branch from the target head when absent, else updating it, committing the edited manifests as `edit` changes
- [ ] 3.3 Add `EnsurePullRequestAsync(repo, sourceBranch, targetBranch, title, description)` — reuse the active PR from the branch if present, else create one; return its URL
- [ ] 3.4 Implement all three via REST on the existing typed `HttpClient` (no clone); model the minimal request/response shapes

## 4. Run persistence

- [ ] 4.1 Add `IRemediationRunStore` (+ Table impl) over a `runs` table (`PartitionKey = repositoryId`, `RowKey = runId`); updates serialized as JSON
- [ ] 4.2 Support append/get and list-by-repository; register in `AddInfrastructure`

## 5. Orchestration

- [ ] 5.1 Add `IRemediationRunner` (+ impl) that runs Reading → Analyzing (plan) → Applying (push branch) → CreatingPR (ensure PR), sets the terminal status, and persists the `RemediationRun`; empty plan → `NoUpdates` (no branch/PR); errors → `Failed`
- [ ] 5.2 Build the PR title/description (markdown summary of package → from → to) from the plan
- [ ] 5.3 Register the runner and planner in `AddInfrastructure`

## 6. Worker wiring

- [ ] 6.1 Replace `Worker.Remediation`'s placeholder handler body to deserialize `RemediationRunRequested` and call `IRemediationRunner.RunAsync`, logging the terminal status/PR link (leave `IRemediationAgent` in place for Slice 5)

## 7. Docs & roadmap

- [ ] 7.1 Update the Key Vault/PAT note (`infra/terraform/README.md`) — the PAT now needs Code (Read & Write) + Pull Request contribute
- [ ] 7.2 Update `ROADMAP.md` — Slice 2 uses Option B (no local build; ADO CI validates the PR)

## 8. Tests

- [ ] 8.1 `ManifestEditor` unit tests (CPM + .csproj bump; unrelated content/other packages unchanged; absent package → no change)
- [ ] 8.2 `IUpdatePlanner` tests with mocked ADO/feed (matched-outdated → updates + changed manifests; nothing outdated → empty plan; unknown versions skipped)
- [ ] 8.3 `RemediationRunner` tests with mocked ADO client + run store: Completed (PR url recorded), NoUpdates (no push/PR), Failed (error recorded, no success)
- [ ] 8.4 ADO write client tests against a stubbed `HttpMessageHandler` (push creates vs updates branch; ensure-PR reuses vs creates)
- [ ] 8.5 Run-store round-trip test against Azurite (skips when the emulator is not running)

## 9. Verification

- [ ] 9.1 `dotnet build AutoRemediator.sln` — zero errors
- [ ] 9.2 `dotnet test AutoRemediator.sln` — all tests pass (emulator-dependent tests self-skip)
- [ ] 9.3 (Manual/live) With a PAT with write scope + a real repo, run the remediation worker and confirm a `autoremediator/dependency-updates` branch + PR is opened, and re-running refreshes the same PR
