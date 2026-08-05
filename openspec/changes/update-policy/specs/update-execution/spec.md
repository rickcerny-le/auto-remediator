## MODIFIED Requirements

### Requirement: Compute version bumps to the policy target
For a repository, the system SHALL compute the set of updates to apply as the matched packages whose current version is outdated relative to their **policy target** — each update being `{package id, from version, to (policy target) version}`. Packages that are ignored, up to date under policy, or of unknown version SHALL be skipped, and only matched packages SHALL be considered (no third-party or unmatched packages, and no collateral/sibling bumps in this slice).

#### Scenario: Bump targets the policy version
- **WHEN** updates are computed for a repository with an outdated matched `Orion180.Core` and a `Minor` strategy
- **THEN** the update targets the highest version within the current major (the policy target), not necessarily the absolute latest

#### Scenario: Ignored packages are excluded from updates
- **WHEN** an outdated matched package matches an ignore glob
- **THEN** it is not included in the update set and no manifest change is produced for it

#### Scenario: Nothing to do yields an empty update set
- **WHEN** no matched package is outdated relative to its policy target
- **THEN** the computed update set is empty
