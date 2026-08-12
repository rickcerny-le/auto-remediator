## MODIFIED Requirements

### Requirement: Run detail API
The API SHALL expose a single run by id, including its applied updates (package, from → to, kind, whether beyond policy), the pull-request link, timestamps, status, any error, its verification outcome — the classification (verified, dependency failure, or skipped), the reason when skipped, the persisted structured diagnostics, and a link to the stored verification logs — and, when the remediation loop ran, the number of attempts made and a link to the stored transcript. The API SHALL serve the stored verification log and the stored transcript for a run as plain text, and SHALL respond not-found when the run stored none.

#### Scenario: Detail returns a run's updates
- **WHEN** a client requests a run by its id
- **THEN** it receives that run with its list of applied updates (package, from → to, kind, beyond-policy) and its PR link and error if any

#### Scenario: Detail returns the verification outcome
- **WHEN** a client requests a run for which verification was attempted
- **THEN** it receives the verification classification, the skip reason when applicable, the persisted diagnostics, and a link to the verification logs

#### Scenario: Detail returns the remediation attempts
- **WHEN** a client requests a run in which the remediation loop ran
- **THEN** it receives the attempt count and a link to the transcript

#### Scenario: Verification log is served as plain text
- **WHEN** a client requests the verification log of a run that stored one
- **THEN** the API returns the full log content as `text/plain`

#### Scenario: Transcript is served as plain text
- **WHEN** a client requests the transcript of a run that stored one
- **THEN** the API returns the full transcript content as `text/plain`

#### Scenario: Missing verification log is not found
- **WHEN** a client requests the verification log of a run that stored none
- **THEN** the API responds with a not-found result

#### Scenario: Missing run is not found
- **WHEN** a client requests a run id that does not exist
- **THEN** the API responds with a not-found result

### Requirement: Run detail page
The Web app SHALL provide a Run detail page that shows a run's updates table (package, from → to, kind, beyond-policy), the PR link, timestamps, status, any error, and the verification outcome — its classification, the skip reason when applicable, the recorded diagnostics (with file, line, and code where available), and a link to the full verification logs. When the remediation loop ran, the page SHALL additionally show that AI remediation was attempted, how many attempts were made, and a link to the transcript. The page SHALL be reachable from the runs feed.

#### Scenario: Drill into a run
- **WHEN** an operator selects a run from the feed
- **THEN** the detail page shows that run's updates (including whether each was matched or collateral and any beyond-policy escalation), its PR link, and any error

#### Scenario: Diagnostics are readable on a rejected run
- **WHEN** an operator opens a run that verification rejected
- **THEN** the detail page shows the recorded diagnostics with their code, message, and file/line where available, and a link to the full verification logs

#### Scenario: Skipped verification is visible on a completed run
- **WHEN** an operator opens a completed run whose verification was skipped
- **THEN** the detail page shows that verification was skipped and the reason, so the PR is not mistaken for a verified one

#### Scenario: AI remediation is visible on a repaired run
- **WHEN** an operator opens a run that the remediation loop repaired
- **THEN** the detail page shows that AI remediation was attempted, the number of attempts, and a link to the transcript

#### Scenario: A run with no AI involvement shows none
- **WHEN** an operator opens a run in which the remediation loop was never invoked
- **THEN** the detail page shows no remediation attempts or transcript
