# local-verification Specification

## Purpose

Defines how a computed dependency change is verified locally before anything is pushed: materializing the repository's working tree in the job from a REST archive, applying the computed edits, running gated `dotnet restore` → `dotnet build`, classifying the result as verified / dependency-failure / environment-skip, and capturing diagnostics and logs as run artifacts. Local verification is the fast approximate signal; the repository's own Azure DevOps CI remains the authority on the resulting pull request.

## Requirements

### Requirement: Materialize the repository working tree in the job
The system SHALL obtain the repository's full working tree at a specified commit by downloading it as a zip archive over the Azure DevOps REST API and extracting it to a per-run temporary directory. No `git` clone SHALL be performed and no `git` binary SHALL be required. The commit used SHALL be the same base commit the resulting push is based on. The temporary directory SHALL be deleted when the run ends, including on failure and on cancellation.

Extraction SHALL strip a wrapper directory that the archive nests all content under, but SHALL NOT strip a shared top-level directory whose name identifies it as belonging to the repository (for example `src`, `tests`, `lib`, `build`, `tools`), because relocating the tree would invalidate every manifest path. Archive entries resolving outside the workspace root SHALL NOT be written.

#### Scenario: Tree is extracted for verification
- **WHEN** a run has a non-empty update set and has resolved its base commit
- **THEN** the repository tree at that commit is downloaded and extracted to a per-run temporary directory, without cloning

#### Scenario: Temporary directory is always cleaned up
- **WHEN** a run finishes in any terminal status, or throws, or is cancelled
- **THEN** its per-run temporary directory is deleted

#### Scenario: Tree download failure does not fail the run
- **WHEN** the tree archive cannot be downloaded or extracted
- **THEN** verification is reported as skipped with the reason recorded, and the run is not failed on that basis

#### Scenario: An archive wrapper directory is stripped
- **WHEN** every archive entry sits under a single top-level directory named after the repository
- **THEN** that directory is stripped so extracted paths are repository-relative

#### Scenario: A repository kept entirely under one source folder is not relocated
- **WHEN** every archive entry sits under `src/`
- **THEN** `src/` is preserved, so `src/Directory.Packages.props` remains at `src/Directory.Packages.props`

#### Scenario: Entries escaping the workspace root are discarded
- **WHEN** an archive entry resolves to a path outside the workspace root
- **THEN** it is not written

### Requirement: Verification is re-runnable against one workspace
The materialized workspace's lifetime SHALL belong to the run rather than to a single verification call, so a run may verify the same tree repeatedly as edits accumulate. Each verification SHALL compile the tree's current contents, including edits applied since the previous verification.

Cleanup SHALL remain unconditional: the run SHALL delete its workspace on every exit path — success, rejection, exhaustion, unexpected error, and cancellation — as it does when a single verification owns it.

#### Scenario: A second verification sees the first's edits
- **WHEN** a file in the workspace is edited after one verification and verification runs again
- **THEN** the second verification compiles the edited content

#### Scenario: Repeated verification does not re-download the tree
- **WHEN** a run verifies the same workspace more than once
- **THEN** the repository archive is downloaded and extracted once for that run

#### Scenario: The workspace is still always cleaned up
- **WHEN** a run that verified more than once reaches any terminal status, or throws, or is cancelled
- **THEN** its workspace directory is deleted

### Requirement: Apply computed edits into the tree before verifying
The system SHALL write the computed manifest edits into the extracted tree before verification runs, so that verification exercises the proposed change rather than the repository's current state. Nothing SHALL be pushed to Azure DevOps before verification has been attempted.

Every edit SHALL target a file the archive actually contained. When an edit's path is absent from the extracted tree, the system SHALL fail the preparation rather than create the file, and verification SHALL be classified as skipped. Creating the file instead would write the edit to a path nothing builds, compile the unedited tree, and report the change as verified.

#### Scenario: Verification exercises the edited manifests
- **WHEN** the update set bumps a package and the tree has been extracted
- **THEN** the edited manifest contents are written into the tree and restore runs against those edited contents

#### Scenario: No push precedes verification
- **WHEN** a run reaches verification
- **THEN** no commit has yet been pushed and no pull request has yet been created for that run

#### Scenario: An edit for a file absent from the tree is not silently created
- **WHEN** an edit targets a path that the extracted tree does not contain
- **THEN** preparation fails with that path named, and verification is classified as skipped rather than verified

### Requirement: Resolve what to restore and build
The system SHALL resolve the build target explicitly rather than relying on the working directory: the shallowest solution file (`.sln` or `.slnx`) when the tree contains one, otherwise every project file (`*.csproj`). When the tree contains neither, verification SHALL be classified as skipped.

An argument-less `dotnet restore` succeeds only when the working directory holds exactly one project or solution, and repositories that keep neither at the root would otherwise fail for a reason unrelated to the dependency change — silently never being verified.

#### Scenario: A solution is preferred over individual projects
- **WHEN** the tree contains a solution file and several projects
- **THEN** restore and build are invoked against the solution

#### Scenario: The shallowest solution wins
- **WHEN** the tree contains solutions at the root and in a subdirectory
- **THEN** the root solution is used

#### Scenario: Every project is built when there is no solution
- **WHEN** the tree contains projects but no solution file
- **THEN** restore and build are invoked for each project

#### Scenario: A tree with nothing buildable is skipped
- **WHEN** the tree contains neither a solution nor a project
- **THEN** verification is classified as skipped with that reason recorded

### Requirement: Restore gates build
The system SHALL run `dotnet restore` against the edited tree first, and SHALL run `dotnet build` only if restore succeeded. Build SHALL NOT be attempted after a failed restore. Build SHALL be invoked with restore suppressed so restore is not repeated. Tests SHALL NOT be run.

#### Scenario: Build runs only after a successful restore
- **WHEN** restore succeeds for the edited tree
- **THEN** build is executed against the restored tree and no restore is repeated as part of it

#### Scenario: Failed restore short-circuits before build
- **WHEN** restore fails
- **THEN** build is not executed and the restore diagnostics are the reported result

#### Scenario: Tests are not executed
- **WHEN** verification completes in any outcome
- **THEN** no test run was performed by the system

### Requirement: Bounded process execution
Each `dotnet` invocation SHALL capture its combined standard output and standard error, SHALL be bounded by a timeout, and SHALL honour the caller's cancellation token. A timeout SHALL be reported distinctly from a non-zero exit, and a caller-requested cancellation SHALL propagate rather than being reported as a timeout.

#### Scenario: A hung invocation is terminated
- **WHEN** restore or build exceeds its allotted time
- **THEN** the process is terminated and the result is reported as timed out rather than blocking the run

#### Scenario: Caller cancellation is not a timeout
- **WHEN** the run's cancellation token is cancelled during an invocation
- **THEN** cancellation propagates to the caller and is not reported as a timeout

### Requirement: Private feed credentials supplied to restore
The system SHALL make the configured package feeds available to restore by generating a `NuGet.config` in the extracted tree that declares those feeds with credentials derived from the configured Azure DevOps PAT — the same credential already used for feed version resolution. The repository's own configured sources SHALL be preserved after the configured feeds, so a third-party source the repository legitimately needs is not dropped. The generated configuration SHALL be treated as a verification artifact only and SHALL NOT be included in the pushed commit.

#### Scenario: Restore authenticates to the private feed
- **WHEN** verification restores a repository whose packages come from a configured private feed
- **THEN** a `NuGet.config` declaring that feed with PAT credentials is present in the tree and restore resolves the packages

#### Scenario: The repository's own sources survive
- **WHEN** the repository already declares a third-party package source
- **THEN** the generated configuration lists the configured feeds first and that third-party source after them

#### Scenario: Credentials are omitted when no PAT is configured
- **WHEN** no PAT is configured
- **THEN** the generated configuration declares the feeds without credentials

#### Scenario: Generated configuration is not committed
- **WHEN** a verified run pushes its changes
- **THEN** the generated `NuGet.config` is not part of the pushed change set

### Requirement: Classify the verification outcome
The system SHALL classify verification into exactly one of three outcomes:
- **Verified** — restore and build both succeeded.
- **DependencyFailure** — restore or build was rejected by positively-identified dependency or compilation diagnostics (for example NuGet `NU`-coded errors or compiler `CS`-coded errors).
- **Skipped** — verification could not run or its result cannot be trusted (for example the tree could not be downloaded or prepared, nothing buildable was found, the SDK could not satisfy the repository's SDK pin, a feed was unreachable or unauthorized, or a process exceeded its timeout).

When a failure cannot be positively identified as dependency-related, the system SHALL classify it as **Skipped** rather than **DependencyFailure**. Misattributing an environment problem to the bump would stop the system producing pull requests.

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
- **WHEN** restore fails because a configured feed could not be reached or authenticated
- **THEN** the outcome is `Skipped` with the reason recorded, not `DependencyFailure`

#### Scenario: An unsatisfiable SDK pin is skipped
- **WHEN** restore fails because no installed SDK satisfies the repository's `global.json`
- **THEN** the outcome is `Skipped` with the reason recorded

#### Scenario: A timeout is skipped
- **WHEN** restore or build exceeds its allotted time and is terminated
- **THEN** the outcome is `Skipped` with the reason recorded

#### Scenario: Unrecognized failures default to skipped
- **WHEN** restore or build fails without any diagnostic identifiable as a dependency or compilation error
- **THEN** the outcome is `Skipped`

#### Scenario: A rejected change offers nothing for the commit
- **WHEN** the outcome is `DependencyFailure`
- **THEN** no file changes are returned for pushing

### Requirement: Capture diagnostics and logs
The system SHALL parse restore and build output into structured diagnostics carrying at least a code, a message, and — where the output provides them — a repository-relative file path with line and column. Both MSBuild diagnostic forms SHALL be recognized: the located `path(line,column): error CODE: message` form and the project-level `path : error CODE: message` form that carries no position and in which NuGet reports most of its errors. Paths SHALL be normalized to be relative to the repository root so they remain meaningful outside the temporary directory.

A bounded number of distinct diagnostics SHALL be persisted on the run record, and the complete restore and build logs SHALL be stored as run artifacts in blob storage with the run record holding a reference to them. Logs SHALL be stored for every outcome. A log that cannot be stored SHALL NOT change the outcome it describes.

#### Scenario: A compiler diagnostic is structured
- **WHEN** build emits `src/Foo/Bar.cs(42,17): error CS0117: 'Client' has no member 'SubmitAsync'`
- **THEN** a diagnostic is recorded with code `CS0117`, the message, the repository-relative path `src/Foo/Bar.cs`, line 42, and column 17

#### Scenario: A project-level restore diagnostic is structured
- **WHEN** restore emits `src/App/App.csproj : error NU1101: Unable to find package Orion180.Nope.`
- **THEN** a diagnostic is recorded with code `NU1101`, the message, and the project path, with no line or column

#### Scenario: A failed build's log includes the preceding restore output
- **WHEN** restore succeeds and build then fails
- **THEN** the stored log contains both the restore and the build output

#### Scenario: Full logs are stored as artifacts
- **WHEN** verification runs in any outcome, including skipped
- **THEN** the complete output is written to blob storage under a per-run path and the run record references it

#### Scenario: Diagnostics on the run record are bounded
- **WHEN** verification produces a large cascade of diagnostics
- **THEN** only a bounded number of distinct diagnostics is persisted on the run record while the full log remains available as an artifact

### Requirement: Regenerated lock files join the change set
When restore produces or modifies a `packages.lock.json` in the tree, the system SHALL include those files in the pushed change set alongside the edited manifests. Repositories that do not use lock files SHALL be unaffected, and no lock file SHALL be introduced where none existed — restore emits lock files unbidden, and committing one would change the repository's build contract.

#### Scenario: An existing lock file is updated in the commit
- **WHEN** the repository commits a `packages.lock.json` and restore rewrites it to match the bumped versions
- **THEN** the pushed commit includes the regenerated lock file together with the edited manifests

#### Scenario: No lock file is introduced
- **WHEN** the repository does not commit a `packages.lock.json`
- **THEN** the pushed change set contains no lock file, even if restore created one in the tree

#### Scenario: An untouched lock file is not a change
- **WHEN** restore leaves a committed lock file byte-identical
- **THEN** it is not included in the pushed change set
