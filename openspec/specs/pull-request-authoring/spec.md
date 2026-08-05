# pull-request-authoring Specification

## Purpose

Defines how the system pushes bumped manifests to a deterministic per-repository branch and ensures exactly one active pull request summarizing the applied updates, creating nothing when there are no changes.

## Requirements

### Requirement: Push bumped manifests to a per-repo branch
The system SHALL push the edited manifests as a commit to a deterministic per-repository branch named `autoremediator/dependency-updates`, based on the repository's target branch. If the branch does not exist it SHALL be created from the target branch head; if it exists it SHALL be updated.

#### Scenario: Branch is created on first run
- **WHEN** an update run pushes changes for a repository that has no `autoremediator/dependency-updates` branch
- **THEN** the branch is created from the target branch head with a commit containing the edited manifests

#### Scenario: Branch is reused on subsequent runs
- **WHEN** an update run pushes changes for a repository that already has the branch
- **THEN** the same branch is updated rather than a new branch being created

### Requirement: Open or refresh a single pull request
The system SHALL ensure exactly one active pull request from the per-repo branch into the target branch: it SHALL create the pull request if none is active, and reuse the existing one otherwise (the refreshed branch updates it). The pull request SHALL include a summary of the applied updates (package, from → to).

#### Scenario: Pull request is created when none exists
- **WHEN** the branch has been pushed and no active PR from it exists
- **THEN** a pull request is created into the target branch with a title and a description listing the updated packages and their version changes

#### Scenario: Existing pull request is reused
- **WHEN** an active pull request from the branch already exists
- **THEN** no duplicate is created; the existing pull request is reused (its head now reflects the new commit)

### Requirement: No pull request without changes
The system SHALL NOT push a branch or open a pull request when the computed update set is empty.

#### Scenario: Empty updates open nothing
- **WHEN** a run computes an empty update set for a repository
- **THEN** no branch push and no pull request occur for that repository
