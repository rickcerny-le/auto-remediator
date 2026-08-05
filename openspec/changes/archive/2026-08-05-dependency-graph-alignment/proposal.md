## Why

Slice 3 bumps matched packages independently by policy. But an internal family is interdependent: bumping `Orion180.Core 1.4 → 1.5` can require `Orion180.Common ≥ 1.5`, and if the repo also pins `Orion180.Common 1.4`, the resulting set no longer resolves. Slice 3b closes that gap with **compatibility-driven collateral bumps** — using the packages' own dependency metadata from the feed, with no local build (the restore-time class of breakage identified during exploration; compile/API breaks remain Slice 5).

## What Changes

- **Read intra-family dependencies** — for a matched package at a chosen version, read its nuspec dependencies from the feed and keep those that match the configured include patterns (the "family"), with their version ranges.
- **Align the declared family** — over the matched packages **declared in the repo's manifests**, start from each package's chosen version (its policy target if being bumped, else its current pin) and check that every intra-family dependency range is satisfied. When a range is unsatisfied, add a **minimal collateral bump** of the (declared) sibling to the lowest available version that satisfies it. Escalating a sibling changes its own dependencies, so alignment iterates to a fixpoint (bounded).
- **Correctness escalates beyond policy** — if satisfying a dependency requires a version outside the update strategy band, take it anyway (a broken set is worse) and mark the update as a policy escalation.
- **Plan + PR reflect collateral** — the update plan gains collateral bumps (kind = collateral) alongside the primary (kind = matched) bumps; the pull-request summary lists both and notes any policy escalations.

Scope boundary: only siblings **already declared** in the manifests are bumped (undeclared/transitive siblings are resolved by NuGet automatically — nothing to edit). No local build, no compile/API fixes (Slice 5). Third-party (non-family) dependencies are out of scope.

## Capabilities

### New Capabilities
- `dependency-graph-alignment`: Reading intra-family dependency metadata from the feed and computing the minimal set of collateral sibling bumps (with beyond-policy escalation) so the chosen family versions mutually resolve.

### Modified Capabilities
- `update-execution`: The computed update set now includes collateral sibling bumps (in addition to the policy primaries), each tagged as matched or collateral, with the edited manifests reflecting both.

## Impact

- New Domain: `DependencyUpdate` gains a `Kind` (matched | collateral) and a `BeyondPolicy` flag; a new alignment result type.
- New Infrastructure: a package-dependency reader (NuGet.Protocol nuspec dependency groups, unioned across target frameworks, filtered to the family) and a `DependencyAligner` (fixpoint resolution over the declared family). `UpdatePlanner` runs the aligner after computing the policy primaries.
- `RemediationRunner`'s PR summary distinguishes primary vs collateral updates and notes escalations; run history records the collateral bumps.
- Still metadata-only and local-first: the extra reads hit the feed (already part of the target repo's ecosystem); no clone, no build.
