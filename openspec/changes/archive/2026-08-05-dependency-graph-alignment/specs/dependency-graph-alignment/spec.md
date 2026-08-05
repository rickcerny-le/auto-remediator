## ADDED Requirements

### Requirement: Read intra-family dependencies from the feed
For a package at a specific version, the system SHALL read its declared dependencies from the feed and retain those whose ids match the configured include patterns (the family), together with each dependency's version range. Dependencies SHALL be considered across the package's target-framework groups (a dependency required by any group counts). No local clone or build is performed.

#### Scenario: Family dependencies are extracted
- **WHEN** `Orion180.Core 1.5.0`'s feed metadata declares dependencies on `Orion180.Common [1.5.0, )` and `Newtonsoft.Json [13.0.0, )`
- **THEN** only `Orion180.Common [1.5.0, )` is returned (matched by the family patterns); `Newtonsoft.Json` is not

### Requirement: Compute minimal collateral bumps over the declared family
Starting from each matched, manifest-declared package's chosen version (its policy target when being bumped, otherwise its current pin), the system SHALL verify that every intra-family dependency range is satisfied by the chosen version of the depended-on package. When a range is unsatisfied and the depended-on package is declared in the manifests, the system SHALL raise that package's chosen version to the lowest available version satisfying the range (a collateral bump). The process SHALL iterate until no further bumps are needed or a bounded iteration limit is reached. Only declared packages are bumped; undeclared (transitive) dependencies are left to NuGet.

#### Scenario: A collateral bump is added when required
- **WHEN** the primary plan bumps `Orion180.Core` to `1.5.0`, `Orion180.Core 1.5.0` requires `Orion180.Common ≥ 1.5.0`, and the repo pins `Orion180.Common 1.4.0`
- **THEN** the alignment adds a collateral bump of `Orion180.Common` to `1.5.0` (the lowest available version satisfying the range)

#### Scenario: No collateral when the set already resolves
- **WHEN** every intra-family dependency range is already satisfied by the chosen versions
- **THEN** no collateral bumps are added

#### Scenario: Undeclared sibling is not bumped
- **WHEN** a required intra-family dependency is not declared in the repo's manifests
- **THEN** no collateral bump is produced for it (NuGet resolves it transitively)

#### Scenario: Iterative alignment reaches a fixpoint
- **WHEN** bumping one sibling introduces a further unsatisfied intra-family range
- **THEN** alignment continues until all ranges are satisfied or the iteration bound is hit

### Requirement: Correctness may escalate beyond policy
When the lowest version satisfying an intra-family range lies outside the update strategy band, the system SHALL still select it (correctness over policy) and SHALL mark that update as a policy escalation.

#### Scenario: Escalation beyond the strategy band
- **WHEN** the strategy is `Minor` but satisfying a dependency requires `Orion180.Common 2.0.0`
- **THEN** `Orion180.Common` is bumped to `2.0.0` and the update is marked as beyond-policy

### Requirement: Collateral bumps are surfaced
Collateral bumps SHALL be distinguishable from primary (matched) bumps in the update set and run history, and the pull-request summary SHALL list collateral bumps and note any policy escalations.

#### Scenario: PR summary distinguishes collateral and escalations
- **WHEN** a run applies a primary bump and a collateral bump that escalated beyond policy
- **THEN** the pull-request summary lists both, labels the collateral bump, and notes the escalation
