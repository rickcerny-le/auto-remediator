## 1. Domain: run status and verification outcome

- [x] 1.1 Add `Verifying`, `Pushing`, and terminal `VerificationFailed` to `RunStatus` in `AutoRemediator.Domain/RemediationRun.cs`
- [x] 1.2 Add a `VerificationOutcome` type to the domain: classification (`Verified` | `DependencyFailure` | `Skipped`), optional skip reason, bounded structured diagnostics, and a log artifact reference
- [x] 1.3 Add a `VerificationDiagnostic` record: code, message, optional repository-relative path, optional line and column
- [x] 1.4 Extend `RemediationRun` with the verification outcome plus a `VerificationFailed(...)` terminal transition, and thread it through `Restore`
- [x] 1.5 Write `AutoRemediator.Domain.Tests/RemediationRunTests.cs` covering the new transitions and that `VerificationFailed` is distinct from `Failed`

## 2. Persistence

- [ ] 2.1 Persist the verification outcome in `RemediationRunStore` (classification, skip reason, bounded diagnostics, log reference), keeping entity size within Table Storage limits
- [ ] 2.2 Extend `RemediationRunStoreRoundTripTests` to cover verified, dependency-failure, and skipped runs round-tripping with diagnostics and log reference intact

## 3. Azure DevOps: tree download

- [ ] 3.1 Add `GetRepositoryArchiveAsync(repository, commitId, ct)` to `IAzureDevOpsClient`, returning the zip stream
- [ ] 3.2 Implement it in `AzureDevOpsClient` against `items?scopePath=/&versionDescriptor.version={commit}&versionDescriptor.versionType=commit&$format=zip&download=true`, surfacing non-success statuses as failures rather than swallowing them
- [ ] 3.3 Extend `AzureDevOpsClientTests` for the archive request shape and for failure propagation

## 4. Verification: tree materialization

- [ ] 4.1 Create `AutoRemediator.Infrastructure/Verification/` with an `IVerificationWorkspace` abstraction that extracts an archive to a per-run temp directory and disposes it (deleting the directory) on any exit path
- [ ] 4.2 Apply the plan's edited manifest contents into the extracted tree
- [ ] 4.3 Generate a `NuGet.config` at the tree root declaring the configured feeds with PAT credentials (merging the repository's existing sources; `<clear/>` for ordering and credential control), tracked as a verification-only artifact excluded from any commit
- [ ] 4.4 Unit-test workspace extraction, edit application, cleanup on failure and cancellation, and that the generated config is marked non-committable

## 5. Verification: restore and build

- [ ] 5.1 Add a process runner that executes `dotnet` with arguments, captures stdout/stderr, enforces a timeout, and honours the cancellation token
- [ ] 5.2 Run `dotnet restore` against the edited tree; run `dotnet build --no-restore -p:GenerateFullPaths=true --nologo` only on restore success
- [ ] 5.3 Parse output into structured diagnostics (MSBuild `path(line,col): error CODE: message` form and NuGet `NU`-coded errors), normalizing paths to repository-relative
- [ ] 5.4 Implement outcome classification: `Verified`; `DependencyFailure` only on positively-identified dependency/compilation diagnostics; `Skipped` for tree/SDK/feed/timeout failures and as the default for unidentified failures
- [ ] 5.5 Detect `packages.lock.json` files created or modified by restore and expose them as additional file changes, introducing none where none existed
- [ ] 5.6 Write `VerificationServiceTests` over captured sample restore/build output for each classification, diagnostic parsing, path normalization, diagnostic bounding, and lock-file detection

## 6. Run artifacts

- [ ] 6.1 Store the full restore and build logs to blob storage via `IBlobStore` under a per-run path and return the reference for the run record
- [ ] 6.2 Test that logs are written for every outcome, including `Skipped`

## 7. Runner: pipeline restructure

- [ ] 7.1 Split `Applying` in `RemediationRunner`: compute edits and materialize the tree without pushing, keeping the `NoUpdates` short-circuit ahead of any download
- [ ] 7.2 Insert the `Verifying` stage; on `DependencyFailure` end the run in `VerificationFailed` with diagnostics and no push or PR
- [ ] 7.3 Add the `Pushing` stage: push edited manifests plus any regenerated lock file, excluding verification-only artifacts
- [ ] 7.4 On `Skipped`, continue to push and open the PR, recording the skip and its reason
- [ ] 7.5 Include the verification outcome in the PR description — verified locally, or skipped with the reason
- [ ] 7.6 Extend `RemediationRunnerTests` for all three outcomes end to end: verified → PR; dependency failure → no push, no PR, `VerificationFailed`; skipped → PR with a not-verified note

## 8. API and UI

- [ ] 8.1 Extend the run detail DTO in `AutoRemediator.Contracts/Dtos/RunDtos.cs` with the verification outcome, diagnostics, and log link
- [ ] 8.2 Surface it from the run detail endpoint in `AutoRemediator.Api/Features/Runs/RunsEndpoints.cs`
- [ ] 8.3 Extend `RunsEndpointTests` for the verification fields on run detail
- [ ] 8.4 Render the verification outcome on the Run detail page: classification, skip reason, diagnostics table (code, message, file, line), and the log link
- [ ] 8.5 Style `VerificationFailed` distinctly from `Failed` in the runs feed and include it in the status filter

## 9. Container image

- [ ] 9.1 Change the final stage of `src/AutoRemediator.Worker.Remediation/Dockerfile` to `mcr.microsoft.com/dotnet/sdk:10.0`
- [ ] 9.2 Build the image and confirm `dotnet restore` is available inside it and the worker still starts

## 10. Verification of the whole slice

- [ ] 10.1 Run the full test suite and confirm no regressions in analysis, policy, or alignment behavior
- [ ] 10.2 Run end to end locally against a real target repository: confirm a verified run opens a PR, a deliberately-broken bump ends in `VerificationFailed` with no PR, and a repository committing `packages.lock.json` gets a regenerated lock file in its commit
- [ ] 10.3 Confirm the degradation path by making verification impossible (unreachable feed) and observing a `Completed` run with a PR marked not verified
