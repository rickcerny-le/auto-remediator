## ADDED Requirements

### Requirement: Runs list API
The API SHALL expose the persisted remediation runs as a list across all repositories, each item carrying at least the run id, repository, status, started/finished timestamps, update count, and pull-request link (when present). The list SHALL support an optional status filter.

#### Scenario: List returns runs across repositories
- **WHEN** a client requests the runs list
- **THEN** it receives the runs from all configured repositories with their id, repository, status, timestamps, update count, and PR link

#### Scenario: Status filter narrows the list
- **WHEN** a client requests the runs list filtered by a status (e.g. `Completed`)
- **THEN** only runs with that status are returned

### Requirement: Run detail API
The API SHALL expose a single run by id, including its applied updates (package, from → to, kind, whether beyond policy), the pull-request link, timestamps, status, and any error.

#### Scenario: Detail returns a run's updates
- **WHEN** a client requests a run by its id
- **THEN** it receives that run with its list of applied updates (package, from → to, kind, beyond-policy) and its PR link and error if any

#### Scenario: Missing run is not found
- **WHEN** a client requests a run id that does not exist
- **THEN** the API responds with a not-found result

### Requirement: Runs feed page
The Web app SHALL provide a Runs page showing the runs as a feed (repository, status, started, update count, PR link), with a status filter and a Refresh control. Status SHALL be visually distinguishable (e.g. success/failure/held styling).

#### Scenario: Runs feed lists runs with status filter
- **WHEN** an operator opens the Runs page
- **THEN** it lists runs with their repository, status, start time, update count, and PR link, and allows filtering by status and refreshing on demand

### Requirement: Run detail page
The Web app SHALL provide a Run detail page that shows a run's updates table (package, from → to, kind, beyond-policy), the PR link, timestamps, status, and any error, reachable from the runs feed.

#### Scenario: Drill into a run
- **WHEN** an operator selects a run from the feed
- **THEN** the detail page shows that run's updates (including whether each was matched or collateral and any beyond-policy escalation), its PR link, and any error
