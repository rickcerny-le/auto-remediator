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
The system SHALL select the packages to consider by matching package ids against the configured include patterns and removing those matching an exclude pattern, treating patterns as case-insensitive package-id globs (e.g. `Orion180.*`).

#### Scenario: Wildcard selects the matching packages
- **WHEN** the pattern `Orion180.*` is applied to a manifest containing `Orion180.Core`, `Orion180.Data`, and `Newtonsoft.Json`
- **THEN** `Orion180.Core` and `Orion180.Data` are selected and `Newtonsoft.Json` is not

### Requirement: Resolve latest versions from the feed
For each matched package, the system SHALL resolve the latest stable version available from the configured feed (excluding pre-release unless policy allows it), authenticating to the feed with the configured credential.

#### Scenario: Latest stable version is resolved
- **WHEN** a matched package's versions are queried from the feed
- **THEN** the highest stable version is returned as the latest, and pre-release versions are ignored by default

### Requirement: Produce the outdated dependency map
The system SHALL compute, per repository and package, whether the pinned version is outdated relative to the resolved latest, and expose the aggregate as a dependency map (repository × package × current version × latest version × status) via the API, rendered read-only in the UI.

#### Scenario: Outdated packages are flagged
- **WHEN** a matched package's pinned version is lower than the resolved latest
- **THEN** it appears in the dependency map with `current`, `latest`, and an `outdated` status

#### Scenario: Up-to-date packages are shown as current
- **WHEN** a matched package is already at the latest version
- **THEN** it appears in the map with an `up-to-date` status

#### Scenario: Map is viewable in the UI
- **WHEN** an operator opens the dependency map page
- **THEN** they see, across configured repositories, the matched packages with their current and latest versions and outdated status
