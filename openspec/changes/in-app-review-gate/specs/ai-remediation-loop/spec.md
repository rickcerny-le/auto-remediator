## MODIFIED Requirements

### Requirement: Iterative remediation against one working tree
The system SHALL run an attempt loop over a single materialized working tree: the agent is given the current diagnostics and the source they refer to, it proposes file edits, the edits are applied to that tree, and verification runs again. Successive attempts SHALL build on the accumulated edits of previous attempts rather than starting from the original tree.

The loop SHALL end as soon as verification succeeds, or when a bound is reached.

A change repaired by the loop SHALL NOT be pushed by the run that produced it. It SHALL be held for human review and pushed only on approval. The agent's edits SHALL be recorded with the held change so they can be replayed against a fresher tree without asking the model again.

#### Scenario: A repaired change is held rather than pushed
- **WHEN** the agent's edits make the build succeed on a later attempt
- **THEN** the loop ends, the change is held for review with the agent's edits recorded, and no pull request is opened until it is approved

#### Scenario: Attempts accumulate rather than reset
- **WHEN** a second attempt runs after a first attempt edited a file
- **THEN** the second attempt's verification compiles a tree containing the first attempt's edits

#### Scenario: The agent receives the current diagnostics
- **WHEN** an attempt begins
- **THEN** the agent is given the diagnostics from the most recent verification, not those from an earlier attempt

### Requirement: Success criteria are stated per environment
The system's expected repair effectiveness SHALL be documented per environment, acknowledging that a local development model and a deployed model differ in capability at reading a changed API surface and repairing call sites. No automated test SHALL assert a repair rate; tests requiring a specific model response SHALL use a fake chat client.

Because every repair is reviewed by a human before it reaches a repository, the loop's output is a proposal rather than a delivered change, and a weak model costs review effort rather than correctness.

#### Scenario: Tests do not depend on model quality
- **WHEN** the loop's behavior is tested
- **THEN** the tests drive a fake chat client and assert loop mechanics — accumulation, bounds, edit constraints, transcript, terminal states — rather than whether a real model produced a correct fix

#### Scenario: No repair reaches a repository unreviewed
- **WHEN** the loop produces a change that verifies
- **THEN** that change reaches the repository only after a human approved it
