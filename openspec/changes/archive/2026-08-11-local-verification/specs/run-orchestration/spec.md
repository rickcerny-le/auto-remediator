## MODIFIED Requirements

### Requirement: Remediation run state machine
On consuming a `RemediationRunRequested` message, the remediation worker SHALL execute a run through the stages Reading → Analyzing → Applying → Verifying → Pushing → CreatingPR, ending in one terminal status: `Completed` (a PR was opened or refreshed), `NoUpdates` (nothing matched was outdated), `VerificationFailed` (local verification positively rejected the computed change, so no PR was opened), or `Failed` (an unexpected error occurred during the run). The `Applying` stage SHALL produce the edited manifests into the working tree without pushing; pushing SHALL occur only after verification has been attempted. The `NoUpdates` short-circuit SHALL occur before any working tree is materialized, so a run with nothing to do performs no download, restore, or build. No AI/remediation-agent step runs in this slice.

#### Scenario: Successful run reaches Completed
- **WHEN** a run finds matched outdated packages, applies the bumps, verification succeeds, and it opens/refreshes a pull request
- **THEN** the run ends with status `Completed` and records the pull request link

#### Scenario: Nothing to update reaches NoUpdates
- **WHEN** a run finds no matched outdated packages
- **THEN** the run ends with status `NoUpdates`, no working tree is downloaded, no verification runs, and no branch or pull request is created

#### Scenario: A rejected change reaches VerificationFailed
- **WHEN** local verification positively rejects the computed change with dependency or compilation diagnostics
- **THEN** the run ends with status `VerificationFailed`, records the diagnostics, and no branch push or pull request occurs

#### Scenario: Skipped verification still reaches Completed
- **WHEN** verification could not run or its result cannot be trusted
- **THEN** the run continues to push and open the pull request and ends with status `Completed`, recording that verification was skipped

#### Scenario: An error reaches Failed
- **WHEN** an unrecoverable error occurs while reading, pushing, or creating the pull request
- **THEN** the run ends with status `Failed` and records the error, and no partial pull request is reported as success

#### Scenario: A rejected change is not reported as a tool failure
- **WHEN** a run ends because verification rejected the change rather than because the tool errored
- **THEN** its status is `VerificationFailed` and not `Failed`, so the two are distinguishable in run history

### Requirement: Persisted run history
Each run SHALL be persisted to Table Storage with at least its id, repository, status, timestamps, the list of applied updates (package, from → to), the pull request link (when created), any error message, and its verification outcome — the classification, a bounded set of structured diagnostics, and a reference to the stored verification logs. History SHALL be queryable per repository.

#### Scenario: A run is recorded and retrievable
- **WHEN** a run completes (in any terminal status)
- **THEN** a run record exists with its repository, status, timestamps, applied updates, PR link if any, error if any, and verification outcome if verification was attempted, retrievable for that repository

#### Scenario: Verification outcome survives a round trip
- **WHEN** a run that verified, was rejected, or skipped verification is persisted and read back
- **THEN** its verification classification, persisted diagnostics, and log reference are preserved
