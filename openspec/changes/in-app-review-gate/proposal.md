# Review AI-authored changes in the app before opening a pull request

## Why

The remediation loop can now repair a compile break that a dependency bump caused, and it discloses that it did so in the pull request. But the pull request is opened either way — the first time a human sees an AI-authored source edit, it is already public in the target repository, visible to everyone watching that repo, and already consuming a reviewer's attention.

That is the wrong order. A model rewrote source in someone else's repository; the person operating this system should see that edit before it is published, not after.

The observability the last slice added — diagnostics, transcript, attempt count — is the raw material for exactly that review, but it is currently only ever read *after* the fact, to explain a pull request that already exists.

This change parks an AI-repaired change inside AutoRemediator as a durable **proposal**, shows it in the web app, and opens the pull request only when a human approves it.

## What Changes

### Only AI-authored changes are gated

A run whose change verified without the agent being invoked SHALL behave exactly as it does today: verify, push, open the pull request, no human in the loop. A mechanical version bump that restores and builds needs no judgment, and gating it would mean clicking through dozens of them for nothing — the gate would stop meaning anything.

The gate applies only when the remediation loop contributed source edits. This is the same condition the pull-request disclosure already keys on, so the system gains no new notion of "interesting".

### A proposal outlives its run

A verified, AI-repaired change becomes a persisted proposal — the change set, the base commit it was verified against, the diagnostics that provoked it, the transcript, and a reviewable diff — and the run rests in a new non-terminal `AwaitingReview` status.

The materialized tree is **not** kept. It is deleted when the worker invocation ends, exactly as it is today; the proposal is data, not a directory. Because no human edits files in this loop, every file in a change set is reproducible from the base commit, the update plan and the recorded agent edits, so nothing is lost by discarding the tree.

### Commands, executed by the worker

The web app issues commands; the remediation worker executes them, as it already executes runs:

- **Approve** — push the change set and open the pull request, with the AI disclosure it would have carried anyway.
- **Rebuild** — re-materialize against the current branch head, replay the plan and the recorded agent edits, verify again, and park the refreshed result. This is also the remedy when an approval is rejected for staleness.
- **Retry** — discard the agent's edits and run the remediation loop again from the original diagnostics, for a reviewer who does not want this particular repair.
- **Discard** — end the proposal without pushing.

Approval is asynchronous. The API enqueues; the worker pushes. The UI reports that the push was requested, not that the pull request exists, until the run record says otherwise.

### The scheduler skips a repository that is awaiting review

While a repository has a proposal awaiting review, scheduled runs for it SHALL be skipped and reported as held, rather than starting a second run that would compete for the same update branch. A newer run silently superseding the proposal under a reviewer is worse than not running.

### A failed repair is still the developer's problem

When the loop exhausts its bounds without a building tree, the run ends in `VerificationFailed` exactly as it does today. Nothing is parked, nothing is offered for review, and no pull request is opened. Only changes that build are worth a human's time; review is then about whether the repair is *right*, never about whether it compiles.

## Impact

- **Affected specs:** `ai-remediation-loop`, `run-orchestration`, `pull-request-authoring`, `run-observability-ui`
- **New capability:** `change-review-gate`
- **Affected code:** `RemediationRunner` (parks instead of pushing when the agent contributed), `RemediationRun`/`RunStatus` (the resting state and the proposal reference), `RemediationRunStore`, a proposal store over blob storage, new command messages in `Contracts`, the remediation worker's consumer, the runs API, and the Run detail page.
- **Unchanged:** the mechanical bump path end to end; the unconditional workspace cleanup; the edit-safety boundary; the rule that nothing reaches a repository except as a pull request on the update branch.

## Out of scope

- **Human editing of proposed source in the app.** If a change is beyond what the loop can repair, remediation fails and the developer takes it from there. Editing files in the app would require the workspace to survive a human, which is what makes this change cheap to avoid.
- **User authentication and authorization.** See the risk below; this is deliberately deferred to a separate Entra change.
- **Pull request or CI status.** Nothing polls Azure DevOps for the state of a pull request after it is opened. The repository's own CI remains the authority, as it is today.

## Risks

- **The approve button writes to source control with no user identity.** The app has no notion of a user; `managed-identity-auth` covers service-to-Azure authentication, not who is sitting in front of the web app. Deployed, this is an unauthenticated control that pushes commits and opens pull requests. It is accepted here only because a separate Entra change is planned, and that change SHOULD land before this one is exposed to a deployed environment.
- **A proposal's verification claim ages.** The pull request description states the change was verified locally. That verification happened when the proposal was parked, possibly days earlier and against a branch head that has since moved. The proposal must carry when it was verified and against which commit, and the description must not imply more freshness than it has.
- **A stale approval is rejected rather than silently applied.** Pushes use a compare-and-swap ref update (`oldObjectId`), so a moved update branch fails the push instead of clobbering it. This is the desired behaviour, but it means approval has a real failure path the UI must handle by offering Rebuild rather than reporting an error.
- **Proposals accumulate.** A parked proposal holds its repository out of the schedule indefinitely. Without visibility, a forgotten proposal silently stops a repository being updated — the same class of failure as the model gating the worker, which this system already decided against once.
