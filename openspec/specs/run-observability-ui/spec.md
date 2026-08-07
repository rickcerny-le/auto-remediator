# run-observability-ui Specification

## Purpose

Exposes persisted remediation runs for observability: API endpoints for listing runs across repositories and viewing a single run's detail, plus Web pages presenting a runs feed and a run detail view so operators can inspect run status, applied updates, PR links, and errors.

## Requirements

### Requirement: Runs list API
The API SHALL expose the persisted remediation runs as a list across all repositories, each item carrying at least the run id, repository, status, started/finished timestamps, update count, and pull-request link (when present). The list SHALL support an optional status filter.

#### Scenario: List returns runs across repositories
- **WHEN** a client requests the runs list
- **THEN** it receives the runs from all configured repositories with their id, repository, status, timestamps, update count, and PR link

#### Scenario: Status filter narrows the list
- **WHEN** a client requests the runs list filtered by a status (e.g. `Completed`)
- **THEN** only runs with that status are returned

### Requirement: Run detail API
The API SHALL expose a single run by id, including its applied updates (package, from → to, kind, whether beyond policy), the pull-request link, timestamps, status, any error, and its verification outcome — the classification (verified, dependency failure, or skipped), the reason when skipped, the persisted structured diagnostics, and a link to the stored verification logs. The API SHALL serve the stored verification log for a run as plain text, and SHALL respond not-found when the run stored none.

#### Scenario: Detail returns a run's updates
- **WHEN** a client requests a run by its id
- **THEN** it receives that run with its list of applied updates (package, from → to, kind, beyond-policy) and its PR link and error if any

#### Scenario: Detail returns the verification outcome
- **WHEN** a client requests a run for which verification was attempted
- **THEN** it receives the verification classification, the skip reason when applicable, the persisted diagnostics, and a link to the verification logs

#### Scenario: Verification log is served as plain text
- **WHEN** a client requests the verification log of a run that stored one
- **THEN** the API returns the full log content as `text/plain`

#### Scenario: Missing verification log is not found
- **WHEN** a client requests the verification log of a run that stored none
- **THEN** the API responds with a not-found result

#### Scenario: Missing run is not found
- **WHEN** a client requests a run id that does not exist
- **THEN** the API responds with a not-found result

### Requirement: Runs feed page
The Web app SHALL provide a Runs page showing the runs as a feed (repository, status, started, update count, PR link), with a status filter and a Refresh control. Status SHALL be visually distinguishable (e.g. success/failure/held styling), and a run rejected by verification SHALL be visually distinguishable from a run that failed unexpectedly — the former is a result, the latter a malfunction.

#### Scenario: Runs feed lists runs with status filter
- **WHEN** an operator opens the Runs page
- **THEN** it lists runs with their repository, status, start time, update count, and PR link, and allows filtering by status and refreshing on demand

#### Scenario: Verification failure reads differently from a tool failure
- **WHEN** the feed contains both a `VerificationFailed` run and a `Failed` run
- **THEN** the two are visually distinguishable and both are filterable by status

### Requirement: Run detail page
The Web app SHALL provide a Run detail page that shows a run's updates table (package, from → to, kind, beyond-policy), the PR link, timestamps, status, any error, and the verification outcome — its classification, the skip reason when applicable, the recorded diagnostics (with file, line, and code where available), and a link to the full verification logs. The page SHALL be reachable from the runs feed.

#### Scenario: Drill into a run
- **WHEN** an operator selects a run from the feed
- **THEN** the detail page shows that run's updates (including whether each was matched or collateral and any beyond-policy escalation), its PR link, and any error

#### Scenario: Diagnostics are readable on a rejected run
- **WHEN** an operator opens a run that verification rejected
- **THEN** the detail page shows the recorded diagnostics with their code, message, and file/line where available, and a link to the full verification logs

#### Scenario: Skipped verification is visible on a completed run
- **WHEN** an operator opens a completed run whose verification was skipped
- **THEN** the detail page shows that verification was skipped and the reason, so the PR is not mistaken for a verified one
