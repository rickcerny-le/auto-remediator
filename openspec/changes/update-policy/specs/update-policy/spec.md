## ADDED Requirements

### Requirement: Strategy-based target version selection
Given a package's current version, the versions available on the feed, and a strategy, the system SHALL select the target as the highest available version allowed by the strategy relative to the current version: `Patch` selects the highest with the same major.minor; `Minor` selects the highest with the same major; `Major` selects the highest overall. If no available version is greater than the current version within the strategy, there is no target (the package is up to date under policy).

#### Scenario: Patch strategy stays within major.minor
- **WHEN** current is `1.4.2` with available `1.4.3`, `1.5.0`, `2.1.0` and strategy `Patch`
- **THEN** the target is `1.4.3`

#### Scenario: Minor strategy stays within major
- **WHEN** current is `1.4.2` with available `1.4.3`, `1.5.0`, `2.1.0` and strategy `Minor`
- **THEN** the target is `1.5.0`

#### Scenario: Major strategy takes the highest
- **WHEN** current is `1.4.2` with available `1.4.3`, `1.5.0`, `2.1.0` and strategy `Major`
- **THEN** the target is `2.1.0`

#### Scenario: Already current under policy yields no target
- **WHEN** current is `1.5.0` with available `1.5.0`, `2.0.0` and strategy `Minor`
- **THEN** there is no target (no higher version exists within the strategy)

### Requirement: Ignore list holds matched packages
The system SHALL treat the ignore list as package-ID globs. A matched package whose id matches any ignore glob SHALL be held: it is never selected for a target/update and is reported with an `Ignored` status.

#### Scenario: Ignored package is held
- **WHEN** `Orion180.Legacy.*` is in the ignore list and `Orion180.Legacy.Api` is matched and outdated
- **THEN** it is reported as `Ignored` and no target is selected for it

#### Scenario: Non-ignored matched package is unaffected
- **WHEN** a matched package does not match any ignore glob
- **THEN** it is evaluated normally against the strategy

### Requirement: Pre-release respected in selection
Version selection SHALL exclude pre-release versions unless the policy allows pre-release.

#### Scenario: Pre-release excluded by default
- **WHEN** the available versions include a higher pre-release and policy does not allow pre-release
- **THEN** the pre-release is not selected as a target
