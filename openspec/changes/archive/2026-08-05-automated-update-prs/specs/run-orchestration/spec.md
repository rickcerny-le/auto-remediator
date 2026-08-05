## ADDED Requirements

### Requirement: Remediation run state machine
On consuming a `RemediationRunRequested` message, the remediation worker SHALL execute a run through the stages Reading → Analyzing → Applying → CreatingPR, ending in one terminal status: `Completed` (a PR was opened or refreshed), `NoUpdates` (nothing matched was outdated), or `Failed` (an error occurred during the run). No AI/remediation-agent step runs in this slice.

#### Scenario: Successful run reaches Completed
- **WHEN** a run finds matched outdated packages, applies the bumps, and opens/refreshes a pull request
- **THEN** the run ends with status `Completed` and records the pull request link

#### Scenario: Nothing to update reaches NoUpdates
- **WHEN** a run finds no matched outdated packages
- **THEN** the run ends with status `NoUpdates`, and no branch or pull request is created

#### Scenario: An error reaches Failed
- **WHEN** an unrecoverable error occurs while reading, pushing, or creating the pull request
- **THEN** the run ends with status `Failed` and records the error, and no partial pull request is reported as success

### Requirement: Persisted run history
Each run SHALL be persisted to Table Storage with at least its id, repository, status, timestamps, the list of applied updates (package, from → to), the pull request link (when created), and any error message. History SHALL be queryable per repository.

#### Scenario: A run is recorded and retrievable
- **WHEN** a run completes (in any terminal status)
- **THEN** a run record exists with its repository, status, timestamps, applied updates, PR link if any, and error if any, retrievable for that repository

### Requirement: One run per requested message
Each `RemediationRunRequested` SHALL produce exactly one persisted run; consuming the same logical request SHALL NOT open more than one pull request for that repository (idempotency is preserved by the per-repo branch/PR reuse).

#### Scenario: Re-processing does not duplicate the PR
- **WHEN** a repository is processed again (a new request or re-delivery)
- **THEN** the existing `autoremediator/dependency-updates` branch and its pull request are reused rather than duplicated
