## Context

Slice 3b (collateral), split from Slice 3 during exploration. Policy bumps matched packages independently; this adds the minimal sibling bumps needed for the internal family to still resolve, computed from feed dependency metadata — the restore-time breakage class, no local build. Compile/API breaks stay Slice 5. Reuses Slice-1/3 analysis, the feed resolver, and the manifest editor.

Confirmed decisions: **compatibility-driven** (minimal, only when a range is unsatisfied); **correctness escalates beyond policy** (flagged); **family = the matched include patterns**.

## Goals / Non-Goals

**Goals:**
- Read a package version's intra-family dependencies (ranges) from the feed.
- Over the matched packages **declared in the manifests**, add the minimal collateral bumps so all intra-family ranges are satisfied; iterate to a fixpoint.
- Escalate beyond the strategy band when required for correctness; flag it.
- Tag updates matched vs collateral; surface collateral + escalations in the PR/run.

**Non-Goals:**
- Local build/restore; compile/API fixes (Slice 5).
- Bumping undeclared/transitive siblings (NuGet handles those) or third-party packages.
- A full NuGet resolver — this is a bounded, declared-family alignment, not general SAT solving.

## Decisions

**1. Only declared siblings are bumped.**
Collateral edits a manifest, so it only applies to matched packages already pinned in the repo's manifests. A required sibling that isn't declared is transitive — NuGet resolves it at restore; nothing to edit. This bounds the problem to the family *present in the repo*. Rationale: correct and small; avoids inventing new package references.

**2. Per-version dependency reads via `FindPackageByIdResource.GetDependencyInfoAsync`.**
`IPackageDependencyReader.GetFamilyDependenciesAsync(id, version, feeds, patterns)` fetches the version's `DependencyGroups`, **unions** the `PackageDependency` entries across all target-framework groups, and keeps those whose id matches the family patterns — returning `(depId, VersionRange)`. Rationale: `GetDependencyInfoAsync` is the precise per-version API (same resource family as version listing); unioning across frameworks is conservative (if any TFM needs it, honor it) and avoids guessing the repo's TFM. *Alternative:* resolve a specific framework — needs reliable TFM detection from manifests; deferred.

**3. Fixpoint alignment over a `chosen` map.**
Build `chosen: id → version` = each declared matched package's current pin, overlaid with the policy **primary** targets. Then loop (bounded, e.g. ≤ 16 iterations):
```
changed = false
for each P in chosen with a parseable version:
  for each (depId, range) in familyDeps(P, chosen[P]):
     if depId ∈ chosen and not range.Satisfies(chosen[depId]):
        cand = lowest available version of depId that is ≥ chosen[depId] and range.Satisfies(cand)
        if cand exists and cand > chosen[depId]:
           chosen[depId] = cand;  changed = true
           if cand outside strategy band vs depId's original current → mark beyondPolicy[depId]
if not changed: break
```
Rationale: escalating a sibling can introduce new unsatisfied ranges, so iterate; "lowest satisfying" keeps bumps minimal; the bound guards against pathological cycles. *Alternative:* one pass — misses transitive cascades; rejected.

**4. `DependencyUpdate` gains `Kind` and `BeyondPolicy`.**
`DependencyUpdate(PackageId, From, To, UpdateKind Kind = Matched, bool BeyondPolicy = false)` with `enum UpdateKind { Matched, Collateral }`. Final updates = ids where `chosen ≠ current`: `Kind = Matched` if in the primary plan, else `Collateral`; `BeyondPolicy` from the alignment flag. Defaults keep Slice-2/3 construction compiling. The run store already serializes updates as JSON, so the new fields persist automatically.

**5. Alignment lives in the planner; a new `IDependencyAligner` does the work.**
`UpdatePlanner`: compute primary updates (as today) → build `chosen` → `aligner.AlignAsync(declaredCurrent, chosen, settings)` → `AlignmentResult { Chosen, BeyondPolicy }` → produce the merged update list + edited manifests (via `ManifestEditor` for every changed id). The `RemediationRunner` is unchanged except its PR/summary builder now groups matched vs collateral and notes escalations. Rationale: keeps the runner thin; the planner remains the single place that produces the plan.

**6. PR summary + run history show collateral.**
The PR description gets a Kind column (and an "escalated beyond policy" note); `RemediationRun.Updates` already carries the full list. Rationale: satisfies the surfacing requirement without new persistence.

## Risks / Trade-offs

- **[Framework-union over-bumps]** → Unioning deps across TFMs may pull a bump a specific target framework wouldn't need. Mitigation: conservative and safe (never under-resolves); acceptable for an internal family. Refine to per-TFM later if noisy.
- **[No version satisfies a range]** → A declared sibling may have no available version satisfying a required range (yanked/mispublished). Mitigation: leave it unbumped and record the unresolved conflict on the run; the repo CI / a human handles it (do not fail the whole run).
- **[Heuristic ≠ real restore]** → We only check declared intra-family ranges, not the full transitive graph or third-party conflicts. Mitigation: that's the intended scope; the PR's ADO CI is still the final gate, and third-party/compile breaks are later slices.
- **[Cycle / non-convergence]** → Mutually-tightening ranges could loop. Mitigation: hard iteration bound; if hit, use the best-so-far `chosen` and flag potential incompleteness.
- **[Extra feed calls]** → Dependency reads add per-package feed round-trips. Mitigation: cache `(id, version) → deps` per pass; only read versions actually chosen.

## Migration Plan

Additive. `DependencyUpdate` gains defaulted fields (no breakage). The planner produces a superset of what Slice 3 produced (collateral only appears when required). Rollback = revert; local dev unaffected. No new external dependency (NuGet.Protocol already referenced).

## Open Questions

- Per-TFM dependency resolution vs the framework-union heuristic (start with union; revisit if it over-bumps).
- How to present an *unresolvable* range on the run/PR (start: list it as an unresolved conflict note; no failure).
- Whether a package that is both a primary and a collateral escalation should report as `Matched` (kept) or `Collateral` — starting with `Matched` + `BeyondPolicy` when escalated past its policy target.
