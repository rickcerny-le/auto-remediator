# update-execution Specification

## Purpose

Defines how the system computes version bumps for matched outdated packages and produces edited manifest contents, deferring build/test validation to the repository's own CI without requiring a local clone.

## Requirements

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

### Requirement: Produce edited manifest contents
The system SHALL apply the computed updates by editing the relevant manifest contents (`Directory.Packages.props` `PackageVersion`, `*.csproj` `PackageReference`), setting each updated package's version to its target while leaving all other content unchanged. Editing SHALL operate on the manifest text and SHALL NOT require a local clone or build.

#### Scenario: A package version is updated in place
- **WHEN** an update sets `Orion180.Core` to `2.0.0` in a `Directory.Packages.props` that pinned `1.0.0`
- **THEN** the returned manifest content has `Orion180.Core` at `2.0.0` and every other package and surrounding content unchanged

#### Scenario: Only manifests with a target package are changed
- **WHEN** the update set is applied across a repository's manifests
- **THEN** only manifests that declared an updated package are returned as changed, and manifests without any updated package are not modified

### Requirement: Validation delegated to the repository CI
The system SHALL NOT build or test the repository locally in this slice; correctness of the bump is validated by the repository's own Azure DevOps CI running against the opened pull request.

#### Scenario: No local build is performed
- **WHEN** an update run executes
- **THEN** it produces edited manifests and a pull request without cloning or building the repository locally
