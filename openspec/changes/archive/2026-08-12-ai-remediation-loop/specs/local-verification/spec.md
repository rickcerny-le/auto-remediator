## ADDED Requirements

### Requirement: Verification is re-runnable against one workspace
The materialized workspace's lifetime SHALL belong to the run rather than to a single verification call, so a run may verify the same tree repeatedly as edits accumulate. Each verification SHALL compile the tree's current contents, including edits applied since the previous verification.

Cleanup SHALL remain unconditional: the run SHALL delete its workspace on every exit path — success, rejection, exhaustion, unexpected error, and cancellation — as it does when a single verification owns it.

#### Scenario: A second verification sees the first's edits
- **WHEN** a file in the workspace is edited after one verification and verification runs again
- **THEN** the second verification compiles the edited content

#### Scenario: Repeated verification does not re-download the tree
- **WHEN** a run verifies the same workspace more than once
- **THEN** the repository archive is downloaded and extracted once for that run

#### Scenario: The workspace is still always cleaned up
- **WHEN** a run that verified more than once reaches any terminal status, or throws, or is cancelled
- **THEN** its workspace directory is deleted
