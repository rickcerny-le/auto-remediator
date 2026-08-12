# run-orchestration Specification

## Purpose

Defines the remediation run state machine, persisted run history, and per-message idempotency that orchestrate a single remediation run from a `RemediationRunRequested` message to a terminal status, including the local verification gate that precedes any push.

## Requirements

### Requirement: Remediation run state machine
On consuming a `RemediationRunRequested` message, the remediation worker SHALL execute a run through the stages Reading → Analyzing → Applying → Verifying → Remediating → Pushing → CreatingPR, ending in one terminal status: `Completed` (a PR was opened or refreshed), `NoUpdates` (nothing matched was outdated), `VerificationFailed` (the computed change was rejected and not repaired, so no PR was opened), or `Failed` (an unexpected error occurred during the run). The `Applying` stage SHALL produce the edited manifests into the working tree without pushing; pushing SHALL occur only after verification has been attempted. The `Remediating` stage SHALL run only when verification rejected the change with compile diagnostics, and SHALL be skipped otherwise. The `NoUpdates` short-circuit SHALL occur before any working tree is materialized, so a run with nothing to do performs no download, restore, or build.

#### Scenario: Successful run reaches Completed
- **WHEN** a run finds matched outdated packages, applies the bumps, verification succeeds, and it opens/refreshes a pull request
- **THEN** the run ends with status `Completed` and records the pull request link

#### Scenario: Nothing to update reaches NoUpdates
- **WHEN** a run finds no matched outdated packages
- **THEN** the run ends with status `NoUpdates`, no working tree is downloaded, no verification runs, and no branch or pull request is created

#### Scenario: A repaired change reaches Completed
- **WHEN** verification rejects the change with compile diagnostics and the remediation loop produces edits that verify
- **THEN** the run passes through `Remediating`, ends with status `Completed`, and records the pull request link

#### Scenario: An unrepaired change reaches VerificationFailed
- **WHEN** the remediation loop exhausts its bounds without a building tree
- **THEN** the run ends with status `VerificationFailed`, records the final diagnostics and the attempt count, and no branch push or pull request occurs

#### Scenario: Remediating is skipped when there is nothing to repair
- **WHEN** verification succeeds, is skipped, or rejects the change with only restore-time diagnostics
- **THEN** the run does not enter the `Remediating` stage

#### Scenario: Skipped verification still reaches Completed
- **WHEN** verification could not run or its result cannot be trusted
- **THEN** the run continues to push and open the pull request and ends with status `Completed`, recording that verification was skipped

#### Scenario: An error reaches Failed
- **WHEN** an unrecoverable error occurs while reading, pushing, or creating the pull request
- **THEN** the run ends with status `Failed` and records the error, and no partial pull request is reported as success

#### Scenario: A rejected change is not reported as a tool failure
- **WHEN** a run ends because verification rejected the change rather than because the tool errored
- **THEN** its status is `VerificationFailed` with no error message, and not `Failed`, so the two are distinguishable in run history

### Requirement: Persisted run history
Each run SHALL be persisted to Table Storage with at least its id, repository, status, timestamps, the list of applied updates (package, from → to), the pull request link (when created), any error message, its verification outcome — the classification, the skip reason when applicable, a bounded set of structured diagnostics, and a reference to the stored verification logs — and, when the remediation loop ran, the number of attempts made and a reference to the stored transcript. History SHALL be queryable per repository.

#### Scenario: A run is recorded and retrievable
- **WHEN** a run completes (in any terminal status)
- **THEN** a run record exists with its repository, status, timestamps, applied updates, PR link if any, error if any, and verification outcome if verification was attempted, retrievable for that repository

#### Scenario: Verification outcome survives a round trip
- **WHEN** a run that verified, was rejected, or skipped verification is persisted and read back
- **THEN** its verification classification, skip reason, persisted diagnostics, and log reference are preserved

#### Scenario: Remediation attempts survive a round trip
- **WHEN** a run in which the remediation loop ran is persisted and read back
- **THEN** its attempt count and transcript reference are preserved

#### Scenario: A run that never verified has no outcome
- **WHEN** a run that ended in `NoUpdates` is persisted and read back
- **THEN** it has no verification outcome and no remediation attempts

### Requirement: One run per requested message
Each `RemediationRunRequested` SHALL produce exactly one persisted run; consuming the same logical request SHALL NOT open more than one pull request for that repository (idempotency is preserved by the per-repo branch/PR reuse).

#### Scenario: Re-processing does not duplicate the PR
- **WHEN** a repository is processed again (a new request or re-delivery)
- **THEN** the existing `autoremediator/dependency-updates` branch and its pull request are reused rather than duplicated
