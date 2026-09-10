# change-review-gate Specification

## Purpose

Holds a verified, AI-repaired change for a human decision instead of pushing it automatically. A
change the AI loop repaired never reaches a repository until a person approves it; a mechanical
bump that verified without agent involvement is unaffected and flows exactly as it always has.
Defines the held proposal, the four review commands (approve, rebuild, retry, discard), staleness
handling, and the visibility of a held repository.

## Requirements

### Requirement: The gate fires only when the loop contributed edits and they verified
When the remediation loop applies at least one edit and the resulting change verifies, the run
SHALL rest in `AwaitingReview` instead of advancing to `Pushing`. A change that verifies with no
agent involvement SHALL push and open its pull request in the same run, exactly as before this
capability existed. A run whose loop exhausted its bounds without a building tree SHALL still end
in `VerificationFailed`, holding nothing.

#### Scenario: An agent-repaired, verified change is held
- **WHEN** the remediation loop applies edits and the resulting change verifies
- **THEN** the run ends in `AwaitingReview` with a stored proposal, and the Azure DevOps client records zero pushes and zero pull requests for that run

#### Scenario: A mechanical bump is unaffected
- **WHEN** a change verifies with the remediation loop never invoked
- **THEN** the run pushes and opens its pull request in the same run, reaching `Completed`, exactly as it did before this capability

#### Scenario: An exhausted loop still lands in VerificationFailed
- **WHEN** the loop exhausts its attempt bound without producing a verified change
- **THEN** the run ends in `VerificationFailed`, holds nothing, and opens no pull request

### Requirement: The proposal captures everything a review needs from stored data alone
The system SHALL persist a change proposal carrying every changed file (manifest edits, regenerated
lock files, and agent edits) together with the content each file had at the base commit, the
diagnostics that provoked the repair, the number of repair attempts, the base commit id, the
verification timestamp, and a reference to the loop's transcript. The proposal SHALL be read from
storage alone when rendering a review — never re-fetched from Azure DevOps at review time. A
proposal that cannot be persisted SHALL fail the run rather than resting in `AwaitingReview`
claiming a proposal it does not have.

#### Scenario: The proposal carries before and after content
- **WHEN** a change is held for review
- **THEN** the stored proposal includes, for every changed file, its content before the edit and the content to push, plus the provoking diagnostics, the attempt count, the base commit, and the verification time

#### Scenario: A proposal write failure fails the run
- **WHEN** the proposal cannot be written to storage
- **THEN** the run ends in `Failed` rather than resting in `AwaitingReview`

#### Scenario: Review reads only stored data
- **WHEN** a person opens a held run for review
- **THEN** the before/after content, diagnostics, attempts, and verification time are served from the stored proposal with no call to Azure DevOps

### Requirement: The temporary workspace is deleted on every exit path
Parking a proposal SHALL delete the temporary verification workspace exactly as every other exit
path does. No run — held, approved, rebuilt, retried, or discarded — SHALL leave a workspace
directory behind.

#### Scenario: No workspace survives a held run
- **WHEN** a run ends in `AwaitingReview`
- **THEN** no temporary workspace directory for that run remains on disk

### Requirement: Approve pushes the stored proposal without re-verifying
Approving a held proposal SHALL push exactly the proposal's files, conditioned on the proposal's
stored base commit (a compare-and-swap), open or refresh the pull request, and reach `Completed`.
Approval SHALL NOT re-verify the change and SHALL NOT materialize a working tree. The pull request
SHALL carry the same AI-authorship disclosure and attempt count a mechanical push carries, and
SHALL additionally state the date and commit the change was verified against, as a fact about the
past rather than an implication that it was just verified.

#### Scenario: Approve pushes exactly the proposal's files
- **WHEN** a held proposal is approved
- **THEN** the push contains exactly the proposal's files, uses the proposal's base commit as the compare-and-swap value, and no verification call is made

#### Scenario: The pull request discloses when the change was verified
- **WHEN** an approved proposal's pull request is opened
- **THEN** its description states the verification date and the base commit, in addition to the AI-authorship disclosure and attempt count

### Requirement: A stale approval is refused, not failed
When the update branch has moved since a proposal's base commit, or an edited file no longer
exists upstream, Azure DevOps SHALL refuse the push via the existing compare-and-swap. The system
SHALL classify this refusal as staleness (distinguishing it from a network error or credential
failure) and SHALL leave the run in `AwaitingReview` with a review note naming the cause and a
rebuild remedy — never `Failed`, and never overwriting the newer work with the stale change.

#### Scenario: A stale push is refused, not failed
- **WHEN** approval's push is refused because the update branch moved
- **THEN** the run stays `AwaitingReview` with a review note, records no error, and opens no pull request

#### Scenario: An unrelated push failure still fails loudly
- **WHEN** a push fails for a reason other than the classified staleness responses
- **THEN** the failure is not reclassified as staleness and propagates as it did before this capability

### Requirement: Rebuild re-verifies at the current branch head
Rebuilding a held proposal SHALL materialize the working tree at the branch's current head, replay
the manifest edits and the recorded agent edits into it, and re-verify. On success it SHALL replace
the stored proposal with one based on the new head, clearing any review note. On failure it SHALL
leave the previous proposal in place and set a review note — a broken rebuild SHALL NOT replace a
good proposal.

#### Scenario: A successful rebuild produces a fresh, approvable proposal
- **WHEN** rebuild verifies successfully at the current branch head
- **THEN** the stored proposal is replaced with one whose base commit is the new head, and the run remains `AwaitingReview` with no review note

#### Scenario: A failed rebuild preserves the old proposal
- **WHEN** rebuild's re-verification fails
- **THEN** the previous proposal remains readable and approvable, and the run records a review note explaining the failure

### Requirement: Retry replays only the manifest edits and drops the recorded agent edits
Retrying a held proposal SHALL materialize the working tree at the proposal's stored base commit
(not the current head), replay only the manifest edits, and run the remediation loop again seeded
with the diagnostics that provoked the original repair, spending a fresh token budget. The
recorded agent edits from the previous attempt SHALL be dropped rather than replayed. Retry SHALL
NOT proceed when no remediation agent is available; it SHALL leave the proposal untouched and set
a review note explaining why.

#### Scenario: Retry drops the previous agent edits
- **WHEN** a held proposal is retried
- **THEN** the working tree is materialized at the proposal's stored base commit with only the manifest edits replayed, and the loop is seeded with the stored provoking diagnostics

#### Scenario: Retry spends a fresh token budget
- **WHEN** a proposal is retried after an earlier attempt spent its budget
- **THEN** the retry is not constrained by the earlier attempt's spend

#### Scenario: Retry without an available model changes nothing
- **WHEN** retry is requested and no remediation agent is available
- **THEN** the proposal is left untouched, the run remains `AwaitingReview`, and a review note explains that no model is configured

### Requirement: Discard is terminal and touches the repository not at all
Discarding a held proposal SHALL end the run in the terminal `Discarded` status. Discard SHALL NOT
push, SHALL NOT open a pull request, and SHALL NOT make any Azure DevOps call.

#### Scenario: Discard reaches a terminal state without touching the repository
- **WHEN** a held proposal is discarded
- **THEN** the run ends in `Discarded`, nothing is pushed, no pull request is opened, and no Azure DevOps method is invoked

### Requirement: A command is refused unless the run is AwaitingReview
Every review command SHALL be refused — changing nothing, publishing nothing, and touching no
Azure DevOps method — unless the run's current status is `AwaitingReview`. The refusal SHALL be
authoritative at the point the command actually executes (the worker, on a single-consumer queue),
so two commands racing for the same run resolve to one winner and the other is refused against the
already-resolved proposal.

#### Scenario: A command against a finished run is refused
- **WHEN** a command is issued for a run that is `Completed`, `Discarded`, `Failed`, or otherwise not `AwaitingReview`
- **THEN** nothing changes, no message is published beyond the refusal, and no Azure DevOps method is called

#### Scenario: A race between two commands resolves to one winner
- **WHEN** approve and discard are both issued for the same held run
- **THEN** exactly one succeeds and the other is refused against the now-resolved run

### Requirement: The review commands never execute in the API
The API SHALL only validate and enqueue a review command; it SHALL NOT call Azure DevOps, run
verification, or mutate a run's status. The command executes only in the worker, which owns the
Azure DevOps client. The API's acceptance response SHALL be understood as "requested", not
"completed" — the run record is the authority on whether the push or pull request actually
happened.

#### Scenario: The API only enqueues
- **WHEN** a review command is posted for a held run
- **THEN** the API publishes a command message and returns immediately, without calling Azure DevOps, running verification, or changing the run's status itself

#### Scenario: Acceptance is not completion
- **WHEN** an approve command is accepted
- **THEN** the response reports that a push was requested, and the run record — not the response — reports whether the pull request exists

### Requirement: A held repository receives no new scheduled runs, visibly
A repository with an open proposal SHALL be skipped by the scheduler rather than started again;
the skip SHALL be recorded as a `SkippedHeld` run naming the repository, so the repository reads as
held rather than merely quiet. The held repository and the proposal holding it SHALL be visible
from the run list (filtered by `AwaitingReview`) and from a dedicated panel on the Web app's
overview page.

#### Scenario: A held repository is skipped and the skip is recorded
- **WHEN** the scheduler runs and a repository already has a proposal awaiting review
- **THEN** the repository is not enqueued, nothing is analyzed or verified, and a `SkippedHeld` run is recorded for it

#### Scenario: Held repositories are visible from the overview
- **WHEN** one or more repositories are held for review
- **THEN** the Web app's overview page lists each one with a link to the proposal holding it

### Requirement: The review surface renders entirely from stored data
The Web app's run detail page SHALL show, for a held run, every changed file's before and after
content, the provoking diagnostics, the attempt count, the transcript link, and the verification
time with its base commit — all from the proposal endpoint, with no Azure DevOps call at view time.
A review note SHALL be presented as a recoverable, actionable state (with a Rebuild control) rather
than as an error.

#### Scenario: The review panel shows the full comparison
- **WHEN** an operator opens a held run
- **THEN** the page shows each file's before and after content, the diagnostics that provoked the repair, the attempt count, a transcript link, and the verification time and base commit

#### Scenario: A review note reads as recoverable, not broken
- **WHEN** a held run carries a review note (staleness, a failed rebuild, or no available model)
- **THEN** the page presents it with a corrective action rather than as an error alert
