# 05 Pipeline uses effective settings (runner, review, dependency map)

**Status:** todo
**Feature:** 004-per-repo-settings
**Depends on:** 04

## What to do
Make every place that plans, verifies or maps a repository use that repository's effective settings, obtained from `IEffectiveSettingsProvider`.

1. **`RemediationRunner`**: replace the `ITargetingSettingsStore` dependency with `IEffectiveSettingsProvider`. Call `var effective = await provider.GetAsync(repo, ct)` and pass `effective.Targeting` wherever `settings` went before (`planner.PlanAsync`, `verification.OpenAsync`).
2. **`ReviewCommandHandler`**: make the same replacement for Rebuild (~line 121) and Retry (~line 169). Add an XML-doc note that review commands use the repository's **current** effective settings.
3. **`DependencyMapService`**:
   - Load the enabled repositories, resolve their effective settings with the batch overload, and analyze each repository with its own `Targeting`. Each repository can now have different patterns, feeds and policy.
   - Also skip repositories whose effective patterns are empty (nothing can match), which avoids a pointless manifest read.
4. **Guard.** After the change, `ITargetingSettingsStore` is injected only in `EffectiveSettingsProvider` and `SettingsEndpoints`. Everything else goes through the provider. Grep to confirm.
5. **Tests.** Update the existing fakes (`FakeSettingsStore` in `RemediationRunnerTests`, `ReviewCommandHandlerTests`, `DependencyMapServiceTests`, and `LocalModeEndToEndTests`'s settings setup) to go through the provider. Add:
   - `DependencyMapServiceTests`: two repositories, one with a repo-only pattern `Fabrikam.*` and one inheriting `Contoso.*` only. The map lists the Fabrikam package for the first repository only. A repository with inheritance off and no patterns contributes no entries and is not read.
   - `RemediationRunnerTests`: a repository whose Strategy override is Major gets a plan computed with Major (assert on what the fake planner received).
   - `LocalModeEndToEndTests` (or a new focused end-to-end test on the local git client): a repository with a repo-only pattern produces an update for that package, while the same package in a sibling repository that inherits is not touched.

## Files
- `src/AutoRemediator.Infrastructure/Remediation/RemediationRunner.cs`
- `src/AutoRemediator.Infrastructure/Review/ReviewCommandHandler.cs`
- `src/AutoRemediator.Infrastructure/Analysis/DependencyMapService.cs`
- `tests/AutoRemediator.Infrastructure.Tests/RemediationRunnerTests.cs`, `Review/ReviewCommandHandlerTests.cs`, `DependencyMapServiceTests.cs`, `LocalModeEndToEndTests.cs`

## Out of scope
- Changing the signatures of `IUpdatePlanner`, `IRepositoryAnalyzer`, `IDependencyAligner` or `IVerificationService`. They keep taking `TargetingSettings`.
- Dependency-map caching (a known defect, still out of scope).
- API and UI (tasks 06 and 07).

## Done when
- [ ] Grepping for `ITargetingSettingsStore` in `src/` shows it only in `TargetingSettingsStore.cs`, `EffectiveSettingsProvider.cs`, `InfrastructureExtensions.cs` and `SettingsEndpoints.cs`.
- [ ] The new tests above pass, and all existing runner, review and dependency-map tests pass. They run against an all-inherit repository, so behaviour is unchanged for them.
- [ ] `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass.

## Notes
- `HeldRunSeeder` does not read settings; leave it alone.
- Feeds now differ per repository. `RepositoryAnalyzer`'s versions cache is per `AnalyzeAsync` call, so there is no cross-repository leakage. Confirm that while you are in there.
