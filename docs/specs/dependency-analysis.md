# dependency-analysis Specification

## Purpose

Defines how AutoRemediator turns repository manifests into an actionable outdated-dependency map: parsing `Directory.Packages.props` and `*.csproj` files, matching package ids against configured patterns and excludes, resolving latest versions from the configured feed, and computing per-repository/per-package outdated status exposed through the API and rendered read-only in the UI.

## Requirements

### Requirement: Parse dependency manifests
The system SHALL parse `Directory.Packages.props` (Central Package Management `PackageVersion` entries) and `*.csproj` (`PackageReference` entries) to extract the set of `{package id, version}` pairs declared by a repository. Parsing SHALL tolerate repositories that use either or both formats.

#### Scenario: Extract packages from Central Package Management
- **WHEN** a `Directory.Packages.props` with `PackageVersion` entries is parsed
- **THEN** each package id and its version are extracted

#### Scenario: Extract packages from project files
- **WHEN** a `*.csproj` with `PackageReference` entries (with versions) is parsed
- **THEN** each package id and its version are extracted, and a repository using both formats yields the union

### Requirement: Match packages against configured patterns
The system SHALL select the packages to consider by matching package ids against the configured include patterns and removing those matching an exclude pattern, treating patterns as case-insensitive package-id globs (e.g. `Contoso.*`).

#### Scenario: Wildcard selects the matching packages
- **WHEN** the pattern `Contoso.*` is applied to a manifest containing `Contoso.Core`, `Contoso.Data`, and `Newtonsoft.Json`
- **THEN** `Contoso.Core` and `Contoso.Data` are selected and `Newtonsoft.Json` is not

### Requirement: Resolve target versions from the feed
For each matched package, the system SHALL resolve the versions available from the configured feed and select the **target** version by applying the update policy (strategy relative to the current pin; pre-release excluded unless allowed), authenticating to the feed with the configured credential. The target is the highest version allowed by the strategy that is greater than the current version, or none when the package is already current under policy.

#### Scenario: Target respects the strategy
- **WHEN** a matched package's available versions are queried and the strategy is `Minor`
- **THEN** the resolved target is the highest version within the same major as the current pin (not necessarily the absolute latest)

#### Scenario: Feed auth is used
- **WHEN** versions are queried from a private feed
- **THEN** the configured credential is used to authenticate

### Requirement: Produce the policy-aware dependency map
The system SHALL compute, per repository and matched package, its status relative to the policy target, and expose the aggregate as a dependency map (repository × package × current version × target version × status) via the API, rendered in the UI. Status is one of `Outdated` (current is behind the policy target), `UpToDate` (current equals or exceeds the policy target), `Ignored` (held by the ignore list), or `Unknown` (version could not be determined). The map's "latest"/target column reflects the **policy target**, not the absolute latest.

#### Scenario: Outdated relative to the policy target
- **WHEN** a matched package's current version is below its policy target
- **THEN** it appears with an `Outdated` status and the policy target as its target version

#### Scenario: Up to date under policy
- **WHEN** a matched package is already at (or above) its policy target
- **THEN** it appears as `UpToDate`

#### Scenario: Ignored package is shown as held
- **WHEN** a matched package matches an ignore glob
- **THEN** it appears with an `Ignored` status and no target

#### Scenario: Map is viewable in the UI
- **WHEN** an operator opens the dependency map page
- **THEN** they see, across configured repositories, the matched packages with their current and policy-target versions and status
