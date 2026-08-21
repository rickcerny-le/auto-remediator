## 1. Domain: the resting state and the proposal

- [ ] 1.1 Add `AwaitingReview` (non-terminal) and `Discarded` (terminal) to `RunStatus`, with the invariant that `AwaitingReview` is the only status that is neither in-flight within a run nor terminal
- [ ] 1.2 Model the proposal in the domain: the change set, base commit, verification time, provoking diagnostics, attempt count, transcript reference — and the recorded agent edits, kept separately so `Rebuild` can replay them and `Retry` can drop them
- [ ] 1.3 Record the proposal reference on `RemediationRun`, threaded through `Restore`
- [ ] 1.4 Add the transitions: `Remediating` → `AwaitingReview`, and `AwaitingReview` → `Pushing` / `AwaitingReview` (rebuilt) / `Discarded`
- [ ] 1.5 Unit-test the transitions, including that a mechanical bump never enters `AwaitingReview` and that an exhausted loop still lands in `VerificationFailed`

## 2. Proposal storage

- [ ] 2.1 Store the change set and the recorded agent edits as a run artifact in blob storage — Table Storage cannot hold whole file contents
- [ ] 2.2 Persist the proposal reference and `AwaitingReview` status in `RemediationRunStore`; extend the round-trip tests
- [ ] 2.3 Index the active proposal per repository, so the scheduler can ask whether a repository is held
- [ ] 2.4 Decide and implement how before-content reaches the diff (stored alongside the change set, or read from Azure DevOps at view time) — see the open question in `design.md`
- [ ] 2.5 Test that a proposal round-trips, and that a failed proposal upload never leaves a run claiming a proposal it does not have

## 3. The runner parks instead of pushing

- [ ] 3.1 In `RemediationRunner`, when the loop repaired the change and it verified, persist the proposal and rest in `AwaitingReview` instead of advancing to `Pushing`
- [ ] 3.2 Leave the no-agent path untouched: a change that verified without the agent still pushes and opens its pull request in the same run
- [ ] 3.3 Keep workspace cleanup unconditional — the existing test asserting no workspace directory survives a run must pass unchanged, including for a parked run
- [ ] 3.4 Test both paths end to end with a fake chat client: agent-repaired parks and pushes nothing; mechanical bump pushes as before

## 4. Commands

- [ ] 4.1 Define the command message(s) in `Contracts` — approve, rebuild, retry, discard against a run id
- [ ] 4.2 Consume them in the remediation worker alongside `RemediationRunRequested`
- [ ] 4.3 Implement approve: push the proposal's recorded change set, open or refresh the pull request, reach `Completed`
- [ ] 4.4 Implement rebuild: re-materialize at the current branch head, replay the plan and recorded agent edits, verify, and replace the proposal
- [ ] 4.5 Implement retry: drop the recorded agent edits and run the loop again from the original diagnostics; decide whether this spends a fresh token budget (open question in `design.md`)
- [ ] 4.6 Implement discard: terminal `Discarded`, nothing pushed
- [ ] 4.7 Refuse any command against a run that is not awaiting review
- [ ] 4.8 Test each command, including that a command against a finished run is refused and changes nothing

## 5. Staleness

- [ ] 5.1 Treat a rejected push (moved update branch) as staleness: leave the proposal awaiting review, record it as stale, do not fail the run
- [ ] 5.2 Surface the base commit and verification time on the proposal so its age is legible
- [ ] 5.3 Test that a stale approval overwrites nothing, leaves the proposal reviewable, and is not reported as `Failed`

## 6. The scheduler holds

- [ ] 6.1 Skip a scheduled run for a repository with a proposal awaiting review, recording it as held rather than silently doing nothing
- [ ] 6.2 Make the held state visible — a held repository must be distinguishable from one that simply has not run
- [ ] 6.3 Test that a held repository is skipped, is reported as held, and resumes normally once the proposal is approved or discarded

## 7. API

- [ ] 7.1 Serve a run's proposal: changed files with before/after, diagnostics, attempt count, base commit, verification time, transcript link
- [ ] 7.2 Accept the four commands, enqueueing rather than executing — the API must never push or open a pull request itself
- [ ] 7.3 Refuse commands against runs not awaiting review; not-found for a run holding no proposal
- [ ] 7.4 Extend `RunsEndpointTests`

## 8. Web

- [ ] 8.1 Review surface: the proposed changes as a readable diff, with the diagnostics, attempt count, transcript and verification age
- [ ] 8.2 The four command controls, with results presented as pending until the run record reports them
- [ ] 8.3 Present a stale proposal as needing a rebuild rather than as an error
- [ ] 8.4 Distinguish `AwaitingReview` in the runs feed from finished and failed runs, and link it to the review surface
- [ ] 8.5 Show repositories held for review

## 9. Disclosure

- [ ] 9.1 State the verification's age and base commit in the pull request description of an approved proposal, so a delayed approval does not read as a fresh verification
- [ ] 9.2 Keep the existing AI disclosure on approved proposals, and assert a run with no agent involvement still makes no such claim

## 10. Safety and verification of the whole slice

- [ ] 10.1 Assert as a test that no agent-authored edit reaches a repository without an approval, and that writes still go only to the update branch and always through a pull request
- [ ] 10.2 Assert that a parked proposal holds no workspace on disk
- [ ] 10.3 Run the full test suite and confirm no regression in the mechanical bump path, analysis, policy, alignment, or verification behavior
- [ ] 10.4 Exercise the gate locally end to end against a deliberately broken bump using the local model: park, review, rebuild, approve, and confirm the pull request and its disclosure read correctly
- [ ] 10.5 Document in the README that agent-authored changes wait for a human, that a held repository receives no updates until its proposal is resolved, and that the approve control is unauthenticated until the planned Entra change lands
