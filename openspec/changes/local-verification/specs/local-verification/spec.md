## ADDED Requirements

### Requirement: Materialize the repository working tree in the job
The system SHALL obtain the repository's full working tree at a specified commit by downloading it as a zip archive over the Azure DevOps REST API and extracting it to a per-run temporary directory. No `git` clone SHALL be performed and no `git` binary SHALL be required. The commit used SHALL be the same base commit the resulting push is based on. The temporary directory SHALL be deleted when the run ends, including on failure and on cancellation.

#### Scenario: Tree is extracted for verification
- **WHEN** a run has a non-empty update set and has resolved its base commit
- **THEN** the repository tree at that commit is downloaded and extracted to a per-run temporary directory, without cloning

#### Scenario: Temporary directory is always cleaned up
- **WHEN** a run finishes in any terminal status, or throws, or is cancelled
- **THEN** its per-run temporary directory is deleted

#### Scenario: Tree download failure does not fail the run
- **WHEN** the tree archive cannot be downloaded or extracted
- **THEN** verification is reported as skipped with the reason recorded, and the run is not failed on that basis

### Requirement: Apply computed edits into the tree before verifying
The system SHALL write the computed manifest edits into the extracted tree before verification runs, so that verification exercises the proposed change rather than the repository's current state. Nothing SHALL be pushed to Azure DevOps before verification has been attempted.

#### Scenario: Verification exercises the edited manifests
- **WHEN** the update set bumps a package and the tree has been extracted
- **THEN** the edited manifest contents are written into the tree and restore runs against those edited contents

#### Scenario: No push precedes verification
- **WHEN** a run reaches verification
- **THEN** no commit has yet been pushed and no pull request has yet been created for that run

### Requirement: Restore gates build
The system SHALL run `dotnet restore` against the edited tree first, and SHALL run `dotnet build` only if restore succeeded. Build SHALL NOT be attempted after a failed restore. Tests SHALL NOT be run.

#### Scenario: Build runs only after a successful restore
- **WHEN** restore succeeds for the edited tree
- **THEN** build is executed against the restored tree and no restore is repeated as part of it

#### Scenario: Failed restore short-circuits before build
- **WHEN** restore fails
- **THEN** build is not executed and the restore diagnostics are the reported result

#### Scenario: Tests are not executed
- **WHEN** verification completes in any outcome
- **THEN** no test run was performed by the system

### Requirement: Private feed credentials supplied to restore
The system SHALL make the configured package feeds available to restore by generating a `NuGet.config` in the extracted tree that declares those feeds with credentials derived from the configured Azure DevOps PAT — the same credential already used for feed version resolution. The generated configuration SHALL be treated as a verification artifact only and SHALL NOT be included in the pushed commit.

#### Scenario: Restore authenticates to the private feed
- **WHEN** verification restores a repository whose packages come from a configured private feed
- **THEN** a `NuGet.config` declaring that feed with PAT credentials is present in the tree and restore resolves the packages

#### Scenario: Generated configuration is not committed
- **WHEN** a verified run pushes its changes
- **THEN** the generated `NuGet.config` is not part of the pushed change set

### Requirement: Classify the verification outcome
The system SHALL classify verification into exactly one of three outcomes:
- **Verified** — restore and build both succeeded.
- **DependencyFailure** — restore or build was rejected by positively-identified dependency or compilation diagnostics (for example NuGet `NU`-coded errors or compiler `CS`-coded errors).
- **Skipped** — verification could not run or its result cannot be trusted (for example the tree could not be downloaded, the SDK could not satisfy the repository's SDK pin, a feed was unreachable, or a process exceeded its timeout).

When a failure cannot be positively identified as dependency-related, the system SHALL classify it as **Skipped** rather than **DependencyFailure**.

#### Scenario: Success is Verified
- **WHEN** restore and build both succeed
- **THEN** the outcome is `Verified`

#### Scenario: A restore version conflict is a dependency failure
- **WHEN** restore fails with a NuGet version-conflict or downgrade error caused by the bumped versions
- **THEN** the outcome is `DependencyFailure` and the diagnostics are recorded

#### Scenario: A compile break is a dependency failure
- **WHEN** restore succeeds but build fails with compiler errors
- **THEN** the outcome is `DependencyFailure` and the compiler diagnostics are recorded

#### Scenario: An unreachable feed is skipped, not a failure
- **WHEN** restore fails because a configured feed could not be reached
- **THEN** the outcome is `Skipped` with the reason recorded, not `DependencyFailure`

#### Scenario: A timeout is skipped
- **WHEN** restore or build exceeds its allotted time and is terminated
- **THEN** the outcome is `Skipped` with the reason recorded

#### Scenario: Unrecognized failures default to skipped
- **WHEN** restore or build fails without any diagnostic identifiable as a dependency or compilation error
- **THEN** the outcome is `Skipped`

### Requirement: Capture diagnostics and logs
The system SHALL parse restore and build output into structured diagnostics carrying at least a code, a message, and — where the output provides them — a repository-relative file path with line and column. Paths SHALL be normalized to be relative to the repository root so they remain meaningful outside the temporary directory. A bounded number of distinct diagnostics SHALL be persisted on the run record, and the complete restore and build logs SHALL be stored as run artifacts in blob storage with the run record holding a reference to them.

#### Scenario: A compiler diagnostic is structured
- **WHEN** build emits `src/Foo/Bar.cs(42,17): error CS0117: 'Client' has no member 'SubmitAsync'`
- **THEN** a diagnostic is recorded with code `CS0117`, the message, the repository-relative path `src/Foo/Bar.cs`, line 42, and column 17

#### Scenario: Full logs are stored as artifacts
- **WHEN** verification runs in any outcome
- **THEN** the complete restore and build output is written to blob storage under a per-run path and the run record references it

#### Scenario: Diagnostics on the run record are bounded
- **WHEN** verification produces a large cascade of diagnostics
- **THEN** only a bounded number of distinct diagnostics is persisted on the run record while the full log remains available as an artifact

### Requirement: Regenerated lock files join the change set
When restore produces or modifies a `packages.lock.json` in the tree, the system SHALL include those files in the pushed change set alongside the edited manifests. Repositories that do not use lock files SHALL be unaffected, and no lock file SHALL be introduced where none existed.

#### Scenario: An existing lock file is updated in the commit
- **WHEN** the repository commits a `packages.lock.json` and restore rewrites it to match the bumped versions
- **THEN** the pushed commit includes the regenerated lock file together with the edited manifests

#### Scenario: No lock file is introduced
- **WHEN** the repository does not commit a `packages.lock.json`
- **THEN** the pushed change set contains no lock file
