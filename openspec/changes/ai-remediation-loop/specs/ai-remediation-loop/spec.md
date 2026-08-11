## ADDED Requirements

### Requirement: The agent is invoked only on compile diagnostics
When local verification rejects a change, the system SHALL invoke the remediation agent only if the diagnostics include compiler (`CS`-coded) errors. A rejection carrying only restore-time (`NU`-coded) diagnostics SHALL end the run without invoking the agent, because a version conflict cannot be resolved by editing source and the agent must not be given the opportunity to "fix" it by editing a manifest.

#### Scenario: A compile break invokes the agent
- **WHEN** verification rejects a change with `CS`-coded diagnostics
- **THEN** the remediation agent is invoked with those diagnostics

#### Scenario: A restore conflict does not invoke the agent
- **WHEN** verification rejects a change with only `NU`-coded diagnostics
- **THEN** the agent is not invoked and the run ends in its verification-failed terminal status

#### Scenario: A verified change does not invoke the agent
- **WHEN** verification succeeds
- **THEN** the agent is not invoked and the run proceeds to push

#### Scenario: A skipped verification does not invoke the agent
- **WHEN** verification is skipped because it could not run or be trusted
- **THEN** the agent is not invoked and the run proceeds to push with the change marked unverified

### Requirement: Iterative remediation against one working tree
The system SHALL run an attempt loop over a single materialized working tree: the agent is given the current diagnostics and the source they refer to, it proposes file edits, the edits are applied to that tree, and verification runs again. Successive attempts SHALL build on the accumulated edits of previous attempts rather than starting from the original tree.

The loop SHALL end as soon as verification succeeds, or when a bound is reached.

#### Scenario: A repaired change is verified and pushed
- **WHEN** the agent's edits make the build succeed on a later attempt
- **THEN** the loop ends, the run is treated as verified, and the change is pushed including the agent's source edits

#### Scenario: Attempts accumulate rather than reset
- **WHEN** a second attempt runs after a first attempt edited a file
- **THEN** the second attempt's verification compiles a tree containing the first attempt's edits

#### Scenario: The agent receives the current diagnostics
- **WHEN** an attempt begins
- **THEN** the agent is given the diagnostics from the most recent verification, not those from an earlier attempt

### Requirement: The loop is explicitly bounded
The loop SHALL be bounded by a maximum attempt count, a per-run token budget, and a per-attempt timeout, each configurable. Reaching any bound SHALL end the loop. When the loop ends without a successful verification, the run SHALL reach its verification-failed terminal status, recording the diagnostics from the final attempt, the number of attempts made, and the transcript — never a pull request.

#### Scenario: The attempt cap ends the loop
- **WHEN** the configured maximum number of attempts have all failed to produce a building tree
- **THEN** the loop ends, no pull request is opened, and the run records the attempt count and final diagnostics

#### Scenario: The token budget ends the loop
- **WHEN** the per-run token budget is exhausted partway through the loop
- **THEN** no further attempt is started and the run ends in its verification-failed terminal status

#### Scenario: An attempt timeout does not hang the run
- **WHEN** a model call exceeds the per-attempt timeout
- **THEN** that attempt is abandoned and the loop ends or continues according to the remaining bounds, without the run hanging

#### Scenario: Exhaustion is never worse than not trying
- **WHEN** the loop exhausts its bounds
- **THEN** the run's terminal status, absence of a pull request, and recorded diagnostics match what the same rejection would have produced with no agent at all

### Requirement: Proposed edits are constrained
The system SHALL apply only edits that satisfy all of the following, and SHALL reject any that do not:

- the path resolves inside the run's workspace root;
- the file already exists in the tree (an edit to an absent path is a defect, not a file to create, since it would leave the unedited tree compiling and the change reported as verified);
- the file is not a dependency manifest (`Directory.Packages.props`, a project file's version elements, or a lock file), which belong to version planning and to restore;
- the file is not an artifact generated for verification.

The agent SHALL propose edits only; it SHALL NOT be able to execute arbitrary commands. The only processes the system runs on its behalf are restore and build.

#### Scenario: An edit escaping the workspace is rejected
- **WHEN** the agent proposes an edit whose path resolves outside the workspace root
- **THEN** the edit is not applied and the attempt records the rejection

#### Scenario: An edit to a manifest is rejected
- **WHEN** the agent proposes an edit to `Directory.Packages.props` or to a lock file
- **THEN** the edit is not applied, so version selection cannot be silently overridden

#### Scenario: An edit to a non-existent file is rejected
- **WHEN** the agent proposes an edit to a path not present in the tree
- **THEN** the edit is not applied

#### Scenario: A source edit is applied
- **WHEN** the agent proposes an edit to an existing source file inside the workspace
- **THEN** the edit is applied to that file and included in the next verification

### Requirement: Nothing is pushed outside a pull request
The system SHALL write only to the per-repository update branch and SHALL always deliver changes as a pull request. It SHALL NOT push to a repository's target branch or to any protected branch, whether or not an agent contributed to the change.

#### Scenario: Agent-authored edits still go through a pull request
- **WHEN** a run in which the agent edited source completes successfully
- **THEN** the edits reach the repository as a commit on the per-repository update branch with a pull request into the target branch, and the target branch is not written to directly

### Requirement: The transcript is captured as a run artifact
The system SHALL record the loop's transcript — the diagnostics presented, the edits proposed and whether each was applied or rejected, and the verification result of each attempt — and SHALL store it as a run artifact alongside the verification logs, with the run record holding a reference to it. A transcript that cannot be stored SHALL NOT change the run's outcome.

#### Scenario: A transcript is stored for every run that invoked the agent
- **WHEN** the loop runs, whether it ends in success or exhaustion
- **THEN** the transcript is stored as a run artifact and the run record references it

#### Scenario: Rejected edits appear in the transcript
- **WHEN** an attempt proposes an edit that is rejected by the edit constraints
- **THEN** the transcript records the proposed edit and the reason it was rejected

#### Scenario: A failed transcript upload does not change the outcome
- **WHEN** the transcript cannot be stored
- **THEN** the run's status and pull request are unaffected

### Requirement: The model is supplied as an injected chat client
The agent SHALL be constructed over a chat client injected by the Aspire Azure AI Inference client integration from a named connection reference, and SHALL NOT read a model endpoint or deployment name from its own configuration section. Application code SHALL be identical whether the model runs locally or in Azure; only the AppHost's modelling of the resource differs.

#### Scenario: The agent uses the injected client
- **WHEN** the agent makes a model call
- **THEN** it uses the injected chat client, with no endpoint or deployment name read from an `Agents` configuration section

#### Scenario: Local and deployed code paths are identical
- **WHEN** the system runs locally against a local model and when it runs deployed against Azure
- **THEN** the agent and its registration are unchanged between the two, differing only in the AppHost's resource configuration

### Requirement: Success criteria are stated per environment
The system's expected repair effectiveness SHALL be documented per environment, acknowledging that a local development model and a deployed model differ in capability at reading a changed API surface and repairing call sites. No automated test SHALL assert a repair rate; tests requiring a specific model response SHALL use a fake chat client.

#### Scenario: Tests do not depend on model quality
- **WHEN** the loop's behavior is tested
- **THEN** the tests drive a fake chat client and assert loop mechanics — accumulation, bounds, edit constraints, transcript, terminal states — rather than whether a real model produced a correct fix
