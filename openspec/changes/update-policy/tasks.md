## 1. Version-selection policy

- [x] 1.1 Add `DependencyStatus.Ignored` to the Domain enum
- [x] 1.2 Add a pure `VersionPolicy.SelectTarget(current, availableVersions, strategy)` (Infrastructure, using `NuGetVersion`): highest candidate `> current` within the strategy band (Patch = same major.minor, Minor = same major, Major = any), or null

## 2. Feed resolution returns versions

- [x] 2.1 Change `IFeedVersionResolver` from `GetLatestVersionAsync` → `GetVersionsAsync(packageId, feeds, allowPrerelease)` returning the available (stable-filtered unless prerelease allowed) versions across feeds
- [x] 2.2 Update `FeedVersionResolver` accordingly (per-feed failures still swallowed; de-duplicate across feeds)

## 3. Policy-aware analysis

- [x] 3.1 In `RepositoryAnalyzer`, apply the ignore globs first (matched + ignored → `Ignored`, no feed lookup); for the rest, fetch versions and select the target via `VersionPolicy` using the settings' strategy + prerelease
- [x] 3.2 Compute status against the policy target: `Outdated` when a target exists, `UpToDate` when none and current parses, `Unknown` otherwise; put the policy target in `AnalyzedPackage.LatestVersion`
- [x] 3.3 Confirm `DependencyMapService` and `UpdatePlanner` need no shape change (planner keeps bumping `Outdated` packages to `LatestVersion`; ignored/held are never `Outdated`)

## 4. Config UI

- [x] 4.1 Add an ignore-list input (comma-separated globs) to the Blazor Configuration page and include it when saving `TargetingSettingsDto` (currently hard-coded empty)

## 5. Tests

- [x] 5.1 `VersionPolicy` unit tests (patch/minor/major relative to current; no-higher-within-band → null; prerelease excluded unless allowed)
- [x] 5.2 Update `IFeedVersionResolver` fakes/usages (Slice-1/2 tests) to the `GetVersionsAsync` shape
- [x] 5.3 Analyzer/map tests: policy target chosen per strategy; ignored package → `Ignored`, no target; outdated relative to policy target
- [x] 5.4 Planner test: bumps to the policy target and skips ignored packages

## 6. Verification

- [x] 6.1 `dotnet build AutoRemediator.sln` — zero errors
- [x] 6.2 `dotnet test AutoRemediator.sln` — all tests pass (emulator-dependent tests self-skip)
