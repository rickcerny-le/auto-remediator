# update-execution Specification

## Purpose

Defines how the system computes version bumps for matched outdated packages and produces edited manifest contents, deferring build/test validation to the repository's own CI without requiring a local clone.

## Requirements

### Requirement: Compute version bumps to the policy target with family alignment
For a repository, the system SHALL compute the update set in two parts: (1) the **primary** bumps — matched packages whose current version is outdated relative to their policy target — and (2) the **collateral** bumps produced by intra-family alignment (declared siblings raised to satisfy dependency ranges, possibly escalated beyond policy). Each update SHALL carry `{package id, from version, to version}` plus whether it is a matched (primary) or collateral bump and whether it escalated beyond policy. Ignored, up-to-date, and unknown-version packages SHALL NOT be primary bumps; only matched packages participate; third-party packages are never bumped. The edited manifests SHALL reflect both primary and collateral bumps.

#### Scenario: Primary bump targets the policy version
- **WHEN** updates are computed for a repository with an outdated matched `Orion180.Core` and a `Minor` strategy
- **THEN** the primary update targets the highest version within the current major (the policy target)

#### Scenario: Collateral bump accompanies a primary bump when required
- **WHEN** a primary bump requires a declared sibling to move to satisfy its dependency range
- **THEN** the update set also contains that sibling as a collateral bump, and the edited manifests reflect both

#### Scenario: Ignored packages are excluded from primary bumps
- **WHEN** an outdated matched package matches an ignore glob
- **THEN** it is not included as a primary bump (it may still move as a collateral bump only if required for correctness)

#### Scenario: Nothing to do yields an empty update set
- **WHEN** no matched package is outdated and the family already resolves
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
