# Feature Specification: In-App Review Gate for AI-Authored Changes

**Feature Branch**: `001-in-app-review-gate`

**Created**: 2026-08-21

**Status**: Draft

**Input**: User description: "I want to focus on the in app review work with that old docs you need to, I will create a repo that we can test this on an get a token to use for auth"

## Overview

Today, when the remediation loop repairs a compile break that a dependency update caused, the
pull request opens automatically. The pull request discloses that AI edits are in it, but by
the time anyone reads that disclosure the edits are already published in someone else's
repository — visible to everyone watching it and already consuming a reviewer's attention.

This feature puts the operator of AutoRemediator in front of that decision. An AI-repaired
change is held inside the app as a durable **proposal**, shown with a diff and the evidence
behind it, and the pull request opens only when a person approves it.

Mechanical version bumps — the ones that restore and build with no AI involvement — are not
held. They carry no judgment call, and a gate that fires on everything is one people click
through unread.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Review and approve an AI-repaired change (Priority: P1)

An operator opens AutoRemediator and sees that a dependency update needed an AI repair and is
waiting on them. They read what broke, see the edits the agent made against the original
files, decide the repair is correct, and approve it. The pull request opens in the target
repository, disclosing the AI authorship as it does today.

**Why this priority**: This is the entire point of the feature — a human sees an AI source
edit before it becomes public. Approve is also the path that makes the gate non-blocking; with
review but no approve, the feature only stops work.

**Independent Test**: Run a dependency update against a test repository, engineered so the
bump breaks the build and the loop repairs it. Confirm no pull request appears while the
proposal sits, then approve in the app and confirm the pull request opens with the expected
edits and disclosure.

**Acceptance Scenarios**:

1. **Given** a run whose change was repaired by the loop and then verified, **When** the run finishes, **Then** the run rests in an awaiting-review state, no branch is pushed, and no pull request exists
2. **Given** a proposal awaiting review, **When** the operator opens it, **Then** they see the changed files with before-and-after content, the diagnostics that provoked the repair, the attempt count, the transcript, and when and against which commit it was verified
3. **Given** a proposal awaiting review, **When** the operator approves it, **Then** the app reports that a push was requested, and the change is pushed to the update branch and its pull request opened or refreshed
4. **Given** an approved proposal whose pull request has opened, **When** the operator views the run, **Then** the pull request is linked and the run has reached its completed state

---

### User Story 2 - Mechanical updates still flow untouched (Priority: P1)

An operator enrolls a repository whose dependency updates apply cleanly. Updates continue to
arrive as pull requests with no one in the loop, exactly as before this feature existed.

**Why this priority**: Equal to P1 above because it is what keeps the gate meaningful. If
every update needed approval, the queue would fill with bumps that require no judgment, and
approval would become a reflex. This is also the regression surface: the mechanical path is
the behavior that already works.

**Independent Test**: Run an update against a repository where the bump builds cleanly, with
the AI loop never invoked. Confirm the pull request opens with no approval step and nothing is
held.

**Acceptance Scenarios**:

1. **Given** an update whose change verified without the loop being invoked, **When** the run completes, **Then** the change is pushed and its pull request opened with no human involvement
2. **Given** an update whose loop exhausted its bounds without producing a building tree, **When** the run ends, **Then** it ends in its verification-failed state, nothing is held for review, and no pull request is opened
3. **Given** any run that holds a change for review, **When** the run ends, **Then** its temporary workspace has been deleted, as on every other exit path

---

### User Story 3 - Reject a repair the operator does not want (Priority: P2)

The operator reads a proposal and decides the repair is wrong — the agent worked around the
breaking change instead of adapting to it. They either ask for another attempt, or discard the
proposal so the repository resumes normal updates.

**Why this priority**: Review is only real if "no" is available. Without it the gate is a
speed bump with one exit. Ranked below P1 because a proposal can be left alone in the interim.

**Independent Test**: With a proposal awaiting review, retry it and confirm a different
attempt is produced from the original diagnostics; separately, discard one and confirm nothing
is pushed and the repository resumes scheduled updates.

**Acceptance Scenarios**:

1. **Given** a proposal awaiting review, **When** the operator retries it, **Then** the recorded agent edits are discarded, the loop runs again from the diagnostics that provoked the original repair with a fresh token budget, and the refreshed result is held for review
2. **Given** a proposal awaiting review, **When** the operator discards it, **Then** the run reaches a terminal discarded state, nothing is pushed, and no pull request is opened
3. **Given** a discarded proposal, **When** the repository's next scheduled update comes due, **Then** it proceeds normally

---

### User Story 4 - Recover a proposal whose branch has moved on (Priority: P2)

A proposal sat for a week and the update branch moved in the meantime. The operator approves
it; the push is refused rather than overwriting the newer work. The app tells them the proposal
is stale and offers to rebuild it, which re-does the work against the current head and hands
back a fresh proposal to approve.

**Why this priority**: Human review introduces wall-clock delay, which makes staleness a
routine occurrence rather than an edge case. Ranked below P1 because it only matters once
proposals have started to age.

**Independent Test**: Park a proposal, move the update branch behind its back, then approve.
Confirm the push is refused, nothing is overwritten, the run is not reported as failed, and a
rebuild produces an approvable proposal.

**Acceptance Scenarios**:

1. **Given** a proposal whose update branch has moved since it was verified, **When** the operator approves it, **Then** the push is refused, the branch is not overwritten, and the proposal remains awaiting review
2. **Given** an approval refused for staleness, **When** the operator views the run, **Then** it is presented as stale and needing a rebuild rather than as an unexpected failure
3. **Given** a stale proposal, **When** the operator rebuilds it, **Then** the change is recomputed at the current branch head, re-verified, and replaces the previous proposal
4. **Given** a proposal of any age, **When** its pull request is eventually opened, **Then** the description states when and against which commit the change was verified, without implying that verification is more recent than it is

---

### User Story 5 - See that a repository is held, not just quiet (Priority: P3)

A repository with an open proposal receives no new updates until that proposal is resolved.
The operator can see that this is why the repository has gone quiet, rather than having to
work it out from an absence.

**Why this priority**: A forgotten proposal silently stops a repository from being updated —
the same failure the project already rejected when it refused to gate the worker on model
availability. Ranked P3 because it is a visibility concern, not the mechanism itself.

**Independent Test**: With a proposal awaiting review, trigger a scheduled run for that
repository. Confirm nothing is analyzed or verified, the skip is recorded, and the app reports
the repository as held for review.

**Acceptance Scenarios**:

1. **Given** a repository with a proposal awaiting review, **When** a scheduled update is requested for it, **Then** nothing is analyzed or verified and the run is recorded as held for review
2. **Given** a repository held for review, **When** the operator looks at the app, **Then** it is reported as held with a link to the proposal holding it, rather than simply showing no recent activity
3. **Given** a repository with proposals for two different runs, **When** the scheduler runs, **Then** the repository remains held and is not started twice

### Edge Cases

- **A proposal's artifact fails to store.** The run must not rest in awaiting-review claiming a proposal it cannot produce. If the proposal cannot be persisted, the run fails visibly rather than parking an empty gate.
- **A command arrives for a run that is not awaiting review** — already approved, discarded, or still in flight. The command is refused and changes nothing; a double-clicked approve must not push twice.
- **A command arrives for a run that no longer exists.** Refused, with nothing created.
- **Two commands race for the same proposal** (approve and discard together). One wins; the other is refused against the now-resolved proposal.
- **A proposal is retried repeatedly.** Each retry spends a fresh token budget by design, so cost grows with operator clicks; retries are recorded so that repetition is visible.
- **The agent's repair touches a file that was deleted upstream.** The push is refused rather than resurrecting the file, and is reported as staleness.
- **A rebuild's re-verification fails.** The rebuild produced a change that no longer builds; the run reports that rather than replacing a good proposal with a broken one.
- **The model is unavailable when a retry is issued.** Retry cannot proceed; the proposal is left intact and the operator is told why, consistent with the model being a soft dependency.

## Requirements *(mandatory)*

### Functional Requirements

**Holding a change**

- **FR-001**: The system MUST hold a verified change for human review when, and only when, the remediation loop contributed source edits to it.
- **FR-002**: The system MUST push and open a pull request without human involvement for a change that verified without the loop being invoked.
- **FR-003**: The system MUST NOT hold anything for review when the loop exhausted its bounds without producing a building tree; the run MUST end in its existing verification-failed state.
- **FR-004**: The system MUST delete the temporary workspace on every exit path, including when a change is held for review.

**The proposal**

- **FR-005**: The system MUST persist a held change as a proposal carrying the change set to be pushed, the original content of each changed file, the commit it was verified against, the time it was verified, the diagnostics that provoked the repair, the number of attempts, and a reference to the transcript.
- **FR-006**: A proposal MUST remain readable after the worker invocation that produced it ends, after a process restart, and after the worker scales to zero.
- **FR-007**: The system MUST identify a proposal by its run, and MUST be able to determine which proposal, if any, is currently holding a given repository.
- **FR-008**: The system MUST fail the run visibly, rather than resting in awaiting-review, if a proposal cannot be persisted.

**Reviewing**

- **FR-009**: Operators MUST be able to see, for a proposal awaiting review, each changed file's before-and-after content, the diagnostics that provoked the repair, the attempt count, the transcript, and the commit and time of verification.
- **FR-010**: The system MUST render the before-and-after comparison without depending on the target repository being reachable at view time.
- **FR-011**: Operators MUST be able to see which repositories are currently held for review and which proposal holds each.

**Commands**

- **FR-012**: Operators MUST be able to issue four commands against a proposal awaiting review: approve, rebuild, retry, and discard.
- **FR-013**: The system MUST execute these commands in the remediation worker, not in the process serving the request.
- **FR-014**: Commands MUST be acknowledged as accepted rather than as complete; the run record MUST report the outcome.
- **FR-015**: Approve MUST push the proposal's change set to the per-repository update branch and open or refresh its pull request, carrying the same AI-authorship disclosure it would have carried without this feature.
- **FR-016**: Approve MUST NOT re-verify before pushing.
- **FR-017**: Rebuild MUST recompute the change at the current branch head by replaying the update plan and the recorded agent edits, verify it again, and replace the previous proposal with the refreshed result.
- **FR-018**: Retry MUST discard the recorded agent edits and run the loop again from the diagnostics that provoked the original repair, with a fresh token budget.
- **FR-019**: Discard MUST end the run in a terminal discarded state without pushing anything.
- **FR-020**: The system MUST refuse any command issued against a run that is not awaiting review, and MUST change nothing when it does.

**Staleness**

- **FR-021**: Pushing a proposal MUST remain conditional on the update branch still being at the commit the proposal was based on, so that an approval of a stale proposal fails rather than overwriting newer work.
- **FR-022**: A push refused for staleness MUST leave the proposal awaiting review, MUST NOT mark the run as failed, and MUST be presented as recoverable by rebuilding.
- **FR-023**: A pull request opened from a proposal MUST state when and against which commit the change was verified, and MUST NOT imply the verification is more recent than it is.

**Scheduling**

- **FR-024**: The system MUST skip a scheduled run for a repository that has a proposal awaiting review, without analyzing or verifying anything.
- **FR-025**: The system MUST record a skipped run and report the repository as held for review.
- **FR-026**: The system MUST allow scheduled runs for a repository to resume once its proposal is approved or discarded.

### Key Entities

- **Proposal**: A verified, AI-repaired change held for a human decision. Carries the changed files with their original and proposed content, the base commit and verification time, the provoking diagnostics, the attempt count, and a transcript reference. Belongs to one run; at most one is active per repository. Outlives the worker that produced it.
- **Recorded agent edits**: The source edits the loop contributed, kept separately from the finished change set so that rebuild can replay them and retry can drop them.
- **Review command**: An operator's decision against a proposal — approve, rebuild, retry, or discard — accepted by the app and executed by the worker.
- **Run status**: Gains a non-terminal resting state for a run awaiting review, and a terminal state for a discarded proposal. Existing statuses keep their meanings.
- **Held repository**: A repository whose scheduled updates are paused because a proposal is awaiting review.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No AI-authored source edit reaches the target repository without a person having approved it — verified by exercising the repaired path end to end and confirming zero pull requests exist before approval.
- **SC-002**: 100% of changes that verify without AI involvement are delivered with no human step, measured against the mechanical-bump path before and after this feature.
- **SC-003**: An operator can reach a decision on a proposal — see what broke, what changed, and how it was repaired — from a single screen, without consulting the target repository or any other tool.
- **SC-004**: A proposal remains reviewable and approvable after the system has been fully restarted.
- **SC-005**: Approving a stale proposal never changes the target repository, and the operator can recover it by rebuilding without hand-editing anything.
- **SC-006**: An operator can tell, at a glance, every repository currently paused for review and why.
- **SC-007**: Operators are told a push was requested within a second of approving, without waiting for the pull request to exist.
- **SC-008**: Every behavior above is demonstrated against a real Azure DevOps repository, and the deterministic parts are covered by tests that do not require a model.

## Assumptions

- **Only verified changes are ever held.** A proposal always builds, so review is about whether the repair is *right*, never whether it compiles. This keeps the exhaustion path exactly as it is.
- **The working tree is not preserved.** Every file in a change set is reproducible from the base commit, the update plan, and the recorded agent edits, because no human edits files in this loop. Discarding the tree therefore loses nothing, and unconditional workspace cleanup is preserved.
- **Original file content is stored with the proposal** rather than fetched at review time, so review is fast, works offline from the target repository, and shows exactly what was compared at verification.
- **Retry spends a fresh token budget**, on the grounds that a human explicitly asking for another attempt is a new unit of work. Total cost is bounded by operator clicks rather than by automation.
- **Approval does not re-verify.** The conditional push already makes a stale approval fail safely, rebuild is the remedy, and the pull request states its verification date honestly.
- **A proposal is keyed by its run**, with a separate way to find the active proposal for a repository — following from the rule that a held repository is skipped.
- **One active proposal per repository at a time**, which follows from skipping scheduled runs while one is open.
- **Commands are asynchronous.** The app enqueues and the worker executes, because pushing belongs to the worker that owns the Azure DevOps client, exactly as verification does.
- **Existing observability is the review material.** Diagnostics, transcript, and attempt count already exist; this feature reads them before the fact instead of after.
- **Testing uses a real Azure DevOps repository and a personal access token**, which the operator will supply. The token needs Code (Read & Write) and pull-request permission, since approval pushes a branch and opens a pull request — broader than the read-only scopes the current configuration documents. Authentication continues to use the existing configuration path; no new auth mechanism is introduced.
- **The model remains a soft dependency.** Without it, no repairs happen, so nothing is held for review and updates continue to flow mechanically.

## Dependencies

- The remediation loop, its transcripts, diagnostics and attempt counts, as already built.
- The existing per-repository update branch and pull-request authoring, including the AI-authorship disclosure.
- The existing conditional (compare-and-swap) push against the branch head.
- Durable storage for run records and for larger run artifacts.
- The existing queue between the app and the remediation worker.
- An Azure DevOps repository and token supplied by the operator for end-to-end testing.

## Out of Scope

- **Editing proposed source in the app.** If a change is beyond what the loop can repair, remediation fails and a developer takes it from there. Human editing would require the workspace to survive a person, which is what makes this feature cheap to build.
- **User authentication and authorization.** The app has no notion of who is using it. See the risk below.
- **Pull request or CI status after opening.** Nothing polls the target repository for what happens to a pull request once it exists; that repository's own CI remains the authority.
- **Notifying anyone that a proposal is waiting.** Proposals are visible in the app; no email, chat, or webhook is sent.

## Risks

- **Approval writes to source control with no user identity.** The app cannot say who approved a proposal, so deployed, this is an unauthenticated control that pushes commits and opens pull requests. Accepted here only because user authentication is planned separately, and that work SHOULD land before this is exposed in a deployed environment. Locally, this is not a concern.
- **A forgotten proposal starves a repository.** A held repository receives no updates until someone acts. Surfacing the held state (FR-011, FR-025) mitigates but does not eliminate this; no automatic expiry is specified.
- **The verification claim ages.** A pull request opened from an old proposal describes a build that happened days earlier. FR-023 makes this honest rather than fresh, but it stays a real gap between what was verified and what is pushed.
- **Retry cost is operator-driven.** A fresh budget per retry means a determined operator can spend arbitrarily on one stubborn proposal. Retries are recorded so the pattern is visible.
