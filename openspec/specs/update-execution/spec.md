# update-execution Specification

## Purpose

Defines how the system computes version bumps for matched outdated packages and produces edited manifest contents, deferring build/test validation to the repository's own CI without requiring a local clone.

## Requirements

### Requirement: Compute version bumps for matched outdated packages
For a repository, the system SHALL compute the set of updates to apply as exactly the matched packages (per the configured patterns) whose pinned version is outdated relative to the resolved latest — each update being `{package id, from version, to version}`. Packages with unknown current or latest versions SHALL be skipped, and only matched packages SHALL be considered (no third-party or unmatched packages in this slice).

#### Scenario: Only matched outdated packages become updates
- **WHEN** the updates are computed for a repository whose manifest contains an outdated `Orion180.Core`, an up-to-date `Orion180.Data`, and an outdated `Newtonsoft.Json`
- **THEN** the update set contains only `Orion180.Core` (from its pinned version to the latest), and excludes the up-to-date and unmatched packages

#### Scenario: Nothing to do yields an empty update set
- **WHEN** no matched package is outdated
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
