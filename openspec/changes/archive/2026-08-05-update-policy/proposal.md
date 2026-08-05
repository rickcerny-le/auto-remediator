## Why

Today the tool always bumps matched outdated packages to the absolute latest version. That is too blunt: teams want to control how aggressive updates are (only patch releases, hold at the current major, or take everything) and to hold specific packages back entirely. The domain already carries these settings (`UpdatePolicy` with a strategy and an ignore list, defined in Slice 1) but nothing consumes them. Slice 3 gives policy teeth — it changes which version a package is targeted at and which packages are updated at all. It needs no build signal, so it fits cleanly after the no-local-build decision in Slice 2.

## What Changes

- **Update strategy** — introduce a version-selection rule that, given a package's current pinned version, the versions available on the feed, and the configured strategy, picks the **target** version:
  - `Patch` → highest release with the same major.minor as current.
  - `Minor` → highest release with the same major as current.
  - `Major` → highest release overall.
  Selection is **relative to the current pin** (standard SemVer-range semantics), honoring the existing pre-release policy.
- **Ignore list** — package-ID globs (e.g. `Orion180.Legacy.*`, consistent with the include/exclude patterns). Matched packages that also match an ignore glob are **held**: shown in the dependency map with an `Ignored` status but never targeted for an update.
- **Analysis & planning honor policy** — the dependency map's target/outdated status now reflects the policy target (not the absolute latest), and the update planner bumps each outdated package to its **policy target** and skips ignored packages.
- **Config UI** — wire the ignore list into the Blazor configuration page (the strategy dropdown already exists; ignore was not yet editable).

Scope boundary: **policy only.** No collateral/sibling bumps (split out to Slice 3b — resolved from feed dependency metadata), no local build, no AI. Policy remains **global** for now (the model is shaped for a later per-repo override).

## Capabilities

### New Capabilities
- `update-policy`: The version-selection rule — strategy (patch/minor/major relative to the current pin) plus ignore-glob handling — that determines each matched package's target version, or that it is held.

### Modified Capabilities
- `dependency-analysis`: Feed resolution returns the candidate versions; the target and outdated status are computed via `update-policy` (policy target, not absolute latest); ignored packages are surfaced as `Ignored`.
- `update-execution`: The planner bumps each outdated package to its policy target and excludes ignored packages.

## Impact

- New Domain/Infrastructure: a pure `VersionPolicy` selector (strategy → target) and an `Ignored` dependency status.
- `IFeedVersionResolver` changes from "return the single latest" to "return the available versions"; the analyzer applies `VersionPolicy` to pick the target. Consuming tests/fakes update accordingly.
- `RepositoryAnalyzer`, `DependencyMapService`, and `UpdatePlanner` become policy-aware; the map's target column now means "policy target".
- Blazor config page gains an ignore-list input (completing the Slice-1 targeting settings).
- No build, no ADO writes beyond what Slice 2 already does; runs locally against emulators as before.
