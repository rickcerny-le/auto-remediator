## ADDED Requirements

### Requirement: Proposal review API
The API SHALL expose the proposal held by a run awaiting review — the files it would change with their before and after content, the diagnostics that provoked the repair, the attempt count, the base commit, the time it was verified, and a link to the transcript — and SHALL accept the approve, rebuild, retry and discard commands against it.

Commands SHALL be accepted for execution rather than performed inline: the API SHALL NOT push or open a pull request itself, and SHALL respond that the command was accepted, with the run record reporting the outcome.

A command issued against a run that is not awaiting review SHALL be refused.

#### Scenario: A held proposal is retrievable
- **WHEN** a client requests the proposal of a run awaiting review
- **THEN** it receives the changed files with before and after content, the diagnostics, the attempt count, the base commit, the verification time, and a transcript link

#### Scenario: A command is accepted rather than executed inline
- **WHEN** a client approves a proposal
- **THEN** the API responds that the command was accepted, without having pushed or opened a pull request in the request

#### Scenario: A command against a run not awaiting review is refused
- **WHEN** a client issues a command against a run that is not awaiting review
- **THEN** the API refuses it and the run is unchanged

#### Scenario: A missing proposal is not found
- **WHEN** a client requests the proposal of a run that holds none
- **THEN** the API responds with a not-found result

### Requirement: Proposal review page
The Web app SHALL provide a review surface for a run awaiting review, showing the proposed file changes as a readable diff, the diagnostics that provoked the repair, the attempt count, the transcript, and how old the verification is. It SHALL offer the approve, rebuild, retry and discard commands, and SHALL present the result of a command as pending until the run record reports it.

A proposal whose approval was rejected as stale SHALL be presented as needing a rebuild rather than as an error.

#### Scenario: A reviewer reads the proposed change
- **WHEN** an operator opens a run awaiting review
- **THEN** the page shows each proposed file change as a diff, alongside the diagnostics, the attempt count and the transcript

#### Scenario: Approval is reported as pending
- **WHEN** an operator approves a proposal
- **THEN** the page reports the approval as pending rather than claiming a pull request exists, until the run record reports one

#### Scenario: A stale proposal offers a rebuild
- **WHEN** an approval was rejected because the update branch had moved
- **THEN** the page presents the proposal as stale and offers to rebuild it, rather than reporting an unexpected error

#### Scenario: The age of the verification is visible
- **WHEN** an operator opens a proposal
- **THEN** the page shows when it was verified and the commit it was verified against

### Requirement: Repositories held for review are visible
The Web app and the runs API SHALL make visible that a repository is held out of the schedule because it has a proposal awaiting review, so a held repository is distinguishable from one that simply has not run.

#### Scenario: A held repository is distinguishable
- **WHEN** a repository has a proposal awaiting review
- **THEN** it is reported as held for review rather than appearing to have no recent activity

#### Scenario: A skipped scheduled run is recorded
- **WHEN** a scheduled run is skipped because the repository is held
- **THEN** that is visible in the runs feed with the reason

## MODIFIED Requirements

### Requirement: Runs feed page
The Web app SHALL provide a Runs page showing the runs as a feed (repository, status, started, update count, PR link), with a status filter and a Refresh control. Status SHALL be visually distinguishable (e.g. success/failure/held styling), and a run rejected by verification SHALL be visually distinguishable from a run that failed unexpectedly — the former is a result, the latter a malfunction.

A run awaiting review SHALL be distinguishable from both, as it is neither finished nor failed, and SHALL be reachable from the feed to its review surface.

#### Scenario: Runs feed lists runs with status filter
- **WHEN** an operator opens the Runs page
- **THEN** it lists runs with their repository, status, start time, update count, and PR link, and allows filtering by status and refreshing on demand

#### Scenario: Verification failure reads differently from a tool failure
- **WHEN** the feed contains both a `VerificationFailed` run and a `Failed` run
- **THEN** the two are visually distinguishable and both are filterable by status

#### Scenario: A run awaiting review is actionable from the feed
- **WHEN** the feed contains a run awaiting review
- **THEN** it is visually distinct from finished and failed runs and links to its review surface
