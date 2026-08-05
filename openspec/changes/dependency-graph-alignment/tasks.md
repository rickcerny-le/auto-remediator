## 1. Domain

- [x] 1.1 Add `enum UpdateKind { Matched, Collateral }`
- [x] 1.2 Extend `DependencyUpdate` with `Kind` (default `Matched`) and `BeyondPolicy` (default `false`), keeping existing construction compiling

## 2. Feed dependency reads

- [x] 2.1 Add `IPackageDependencyReader.GetFamilyDependenciesAsync(packageId, version, feeds, patterns)` → `(depId, VersionRange)` list: fetch the version's dependency groups via `FindPackageByIdResource.GetDependencyInfoAsync`, union across TFM groups, keep ids matching the family patterns
- [x] 2.2 Implement it (NuGet.Protocol, PAT auth like the version resolver; per-feed failures swallowed); register in `AddInfrastructure`

## 3. Alignment

- [x] 3.1 Add `IDependencyAligner.AlignAsync(declaredCurrent, chosen, settings)` → `AlignmentResult { Chosen, BeyondPolicy }`
- [x] 3.2 Implement the bounded fixpoint: for each declared package at its chosen version, read family deps; when a declared sibling's chosen version fails a range, raise it to the lowest available satisfying version (≥ its chosen); mark `BeyondPolicy` when outside the strategy band vs the sibling's original current; iterate to a fixpoint or the bound; cache `(id,version)→deps` per pass
- [x] 3.3 Leave an unresolvable range unbumped and record it (no run failure)
- [x] 3.4 Register the aligner in `AddInfrastructure`

## 4. Planner integration

- [x] 4.1 In `UpdatePlanner`: after computing primary bumps, build the declared-matched `current` + `chosen` maps, run the aligner, and produce the merged update list (Kind + BeyondPolicy) plus edited manifests (`ManifestEditor` for every changed id)
- [x] 4.2 Ensure `RepositoryUpdatePlan.HasChanges` and empty-plan behavior still hold (no changes → empty)

## 5. PR summary / run history

- [x] 5.1 Update `RemediationRunner`'s PR title/description to group primary vs collateral updates and note beyond-policy escalations; run history already persists the full update list

## 6. Tests

- [ ] 6.1 `IPackageDependencyReader` filtering test (family deps kept, third-party dropped, union across groups) *(not added as a standalone test — the reader wraps `FindPackageByIdResource` and needs a live feed; its family-filter is `PackagePatternMatcher` (unit-tested) and its contract is exercised by the aligner tests via a fake reader)*
- [x] 6.2 `DependencyAligner` tests with a fake dependency reader + fake version resolver: no collateral when set resolves; a required collateral bump added (minimal); undeclared sibling not bumped; iterative cascade (A→B→C) to fixpoint; escalation beyond policy flagged; unresolvable range left unbumped
- [x] 6.3 `UpdatePlanner` test: primary + collateral merged into the plan with correct kinds; manifests edited for both
- [x] 6.4 PR summary test (or runner test) showing collateral labelled and escalation noted

## 7. Verification

- [x] 7.1 `dotnet build AutoRemediator.sln` — zero errors
- [x] 7.2 `dotnet test AutoRemediator.sln` — all tests pass (emulator-dependent tests self-skip)
