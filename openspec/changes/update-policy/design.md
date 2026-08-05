## Context

Slice 3 of the roadmap — policy depth, split from collateral bumps (Slice 3b) during exploration because policy needs no build signal while collateral does. The domain already has `UpdatePolicy(Strategy, Ignore, AllowPrerelease)` (Slice 1); nothing consumes it yet. Today `FeedVersionResolver` returns the single highest stable version and the analyzer/planner treat "latest" as the bump target. This change makes version selection policy-aware.

Confirmed decisions: strategy is **relative to the current pin** (patch = same major.minor, minor = same major, major = highest); ignore entries are **globs**; policy stays **global** for now; ignored-but-matched packages are surfaced as `Ignored` in the map.

## Goals / Non-Goals

**Goals:**
- A pure, testable version-selection rule (current + available versions + strategy → target | none).
- Ignore-glob handling (held → `Ignored`, never updated).
- Analyzer/map/planner honor policy; map's target column = policy target.
- Ignore-list editing in the config UI.

**Non-Goals:**
- Collateral/sibling bumps (Slice 3b), local build, AI (Slice 5).
- Per-repo policy override (global only; model already allows it later).
- New feed round-trips beyond fetching the version list.

## Decisions

**1. `VersionPolicy` selector lives in Infrastructure (needs `NuGetVersion`), pure and unit-tested.**
`VersionPolicy.SelectTarget(NuGetVersion current, IEnumerable<NuGetVersion> available, UpdateStrategy strategy)` returns the highest candidate `> current` satisfying the strategy band, or null. Bands: `Patch` → `Major == current.Major && Minor == current.Minor`; `Minor` → `Major == current.Major`; `Major` → no band. Rationale: SemVer comparison needs `NuGetVersion` (already referenced in Infrastructure via NuGet.Protocol); keeping it out of Domain avoids pulling NuGet into the pure model. It is still a pure function, tested directly. *Alternative:* a hand-rolled SemVer in Domain — reinvents NuGet's version semantics; rejected.

**2. `IFeedVersionResolver` returns the available versions, not a single latest.**
Change `GetLatestVersionAsync` → `GetVersionsAsync(packageId, feeds, allowPrerelease)` returning the (stable-filtered) `NuGetVersion` list across feeds. The analyzer then applies `VersionPolicy` with the settings' strategy. Rationale: the policy target depends on the current pin, which the resolver doesn't know; returning the set keeps the resolver dumb and the policy in one place. *Trade-off:* callers/fakes change — updated in this slice.

**3. Analyzer owns policy application; ignore handled before resolution.**
`RepositoryAnalyzer`: for each pattern-matched package — if it matches an ignore glob → `Ignored` (no feed lookup, no target); else fetch versions (cached per pass) and `VersionPolicy.SelectTarget(current, versions, strategy)` → target; status `Outdated` if a target exists, `UpToDate` if none and current parses, `Unknown` otherwise. The `AnalyzedPackage.LatestVersion` now carries the **policy target**. Rationale: one place computes target + status; map and planner consume it unchanged in shape.

**4. `Ignored` status added; map/DTO carry it as a string.**
Add `DependencyStatus.Ignored`. The map DTO already stringifies status, so the UI shows "Ignored" with no target. Ignored rows are visible (the ignore list has visibility value) but never planned. Rationale: distinguishes "held" from "excluded" (excludes never appear); small, additive.

**5. Planner unchanged in shape — it already bumps `Outdated` packages to `LatestVersion`.**
Because the analyzer now puts the policy target in `LatestVersion` and never marks ignored/held packages `Outdated`, `UpdatePlanner` needs no logic change beyond continuing to target `LatestVersion`. Rationale: the policy is fully captured upstream; the planner stays a thin projection.

**6. Config UI: add an ignore-list input.**
The Blazor page already binds strategy; add an ignore-list text input (comma-separated globs) and include it when saving `TargetingSettingsDto` (currently hard-coded empty). Rationale: completes the Slice-1 targeting settings the spec already describes.

## Risks / Trade-offs

- **[Interface change ripples to tests]** → `IFeedVersionResolver` signature change breaks Slice-1/2 fakes. Mitigation: update them in this slice; the new shape is simpler to fake (return a list).
- **[Map meaning shifts]** → "latest" now means "policy target"; a package could be `UpToDate` under `Minor` while a newer major exists. Mitigation: documented; a later polish could also surface "a newer out-of-policy version exists". Noted as open.
- **[Unparseable current version]** → strategy bands need a parseable current. Mitigation: unparseable/variable current → `Unknown` (already how the analyzer treats it), never targeted.
- **[Ignore vs exclude confusion]** → both filter packages. Mitigation: excludes remove from the matched set (never shown); ignore holds a matched package (shown as `Ignored`). Documented distinction.

## Migration Plan

Additive/behavioral. No data migration (settings already carry strategy/ignore; default strategy is `Minor`, ignore empty → behavior differs from prior "always latest" only where a non-`Major` strategy or ignore is configured). Rollback = revert; local dev unaffected.

## Open Questions

- Whether the map should also surface "a newer version exists beyond policy" alongside the policy target (deferred; keep one target column now).
- Exact default strategy for existing installs — `UpdatePolicy.Default` is `Minor`; confirm that is the desired default (vs `Major` to preserve prior always-latest behavior).
