## ADDED Requirements

### Requirement: Only agent-authored changes are held for review
The system SHALL hold a verified change for human review only when the remediation loop contributed source edits to it. A change that verified without the agent being invoked SHALL be pushed and delivered as a pull request without human involvement.

Gating every change would require a person to approve version bumps that carry no judgment, and a gate that fires on everything is one that is clicked through unread.

#### Scenario: An agent-repaired change is held
- **WHEN** the remediation loop repaired a compile break and the resulting change verified
- **THEN** the change is held as a proposal awaiting review, and no push or pull request occurs yet

#### Scenario: A mechanical bump is not held
- **WHEN** a run's change verified without the remediation loop being invoked
- **THEN** the change is pushed and its pull request opened without waiting for a human

#### Scenario: An unrepaired change is not held
- **WHEN** the remediation loop exhausted its bounds without producing a building tree
- **THEN** the run ends in its verification-failed terminal status, nothing is held for review, and no pull request is opened

### Requirement: A proposal outlives the run that produced it
A held change SHALL be persisted as a proposal carrying the change set to be pushed, the commit it was verified against, the time it was verified, the diagnostics that provoked the repair, and a reference to the transcript. The proposal SHALL survive the end of the worker invocation, a process restart, and the worker scaling to zero.

The materialized working tree SHALL NOT be retained. Workspace cleanup remains unconditional on every exit path. Because no human edits files in this loop, every file in a change set is reproducible from the base commit, the update plan and the recorded agent edits, so discarding the tree loses nothing.

Change sets SHALL be stored as run artifacts in blob storage rather than on the run record, whose entity and property sizes cannot hold whole file contents.

#### Scenario: The proposal survives the worker
- **WHEN** the worker invocation that produced a held change ends
- **THEN** the proposal remains readable, and the run rests in its awaiting-review status

#### Scenario: The workspace is still deleted
- **WHEN** a run holds a change for review
- **THEN** its workspace directory is deleted, as it is on every other exit path

#### Scenario: The proposal records what it was verified against
- **WHEN** a proposal is read
- **THEN** it carries the base commit and the time of the verification that produced it

### Requirement: A proposal is acted on by explicit command
The system SHALL accept four commands against a proposal awaiting review — approve, rebuild, retry, and discard — and SHALL execute them in the remediation worker rather than in the API process.

- **Approve** SHALL push the proposal's change set and open or refresh the pull request.
- **Rebuild** SHALL re-materialize the tree at the current branch head, replay the update plan and the recorded agent edits, verify again, and hold the refreshed result.
- **Retry** SHALL discard the recorded agent edits and run the remediation loop again from the original diagnostics.
- **Discard** SHALL end the proposal without pushing.

Commands SHALL be asynchronous: the request SHALL be acknowledged as accepted, and the run record SHALL report the outcome.

#### Scenario: Approval pushes and opens the pull request
- **WHEN** a proposal awaiting review is approved
- **THEN** its change set is pushed to the per-repository update branch and the pull request is opened or refreshed

#### Scenario: A command is executed by the worker
- **WHEN** a command is issued against a proposal
- **THEN** the API accepts it without pushing, and the remediation worker performs the work

#### Scenario: Rebuild refreshes a proposal against the current head
- **WHEN** a proposal is rebuilt
- **THEN** the tree is re-materialized at the current branch head, the plan and recorded agent edits are replayed, verification runs again, and the refreshed proposal replaces the previous one

#### Scenario: Retry asks the agent again
- **WHEN** a reviewer retries a proposal
- **THEN** the recorded agent edits are discarded and the remediation loop runs again from the diagnostics that provoked the original repair

#### Scenario: Discard ends the proposal
- **WHEN** a proposal is discarded
- **THEN** the run reaches a terminal discarded status, nothing is pushed, and no pull request is opened

### Requirement: A stale approval fails rather than overwriting
Pushing a proposal SHALL remain a compare-and-swap against the branch head it was based on, so an approval of a proposal whose update branch has since moved SHALL fail rather than overwrite the branch. A failed approval SHALL leave the proposal awaiting review and SHALL be reported as staleness with rebuild offered, not as an unexpected error.

#### Scenario: A moved branch rejects the approval
- **WHEN** a proposal is approved after its update branch has moved
- **THEN** the push is rejected, nothing is overwritten, and the proposal remains awaiting review

#### Scenario: Staleness is reported as recoverable
- **WHEN** an approval fails because the proposal is stale
- **THEN** the run is not failed, and the proposal is presented as needing a rebuild

### Requirement: A repository awaiting review is held out of the schedule
While a repository has a proposal awaiting review, a scheduled run for that repository SHALL be skipped rather than started, so a newer run cannot supersede a proposal while it is being reviewed. A skipped run SHALL be recorded and SHALL be visible as held for review.

A held repository receives no dependency updates until its proposal is resolved. This is the same failure the system already rejected when it refused to gate the worker on the model's availability, so the held state SHALL be surfaced rather than merely absent.

#### Scenario: A scheduled run is skipped while a proposal is open
- **WHEN** a scheduled run is requested for a repository that has a proposal awaiting review
- **THEN** the run does not analyze or verify anything and is recorded as held for review

#### Scenario: A held repository is visible
- **WHEN** a repository is held for review
- **THEN** it is reported as held, rather than simply having no recent runs

#### Scenario: Resolving a proposal releases the repository
- **WHEN** a proposal is approved or discarded
- **THEN** subsequent scheduled runs for that repository proceed normally
