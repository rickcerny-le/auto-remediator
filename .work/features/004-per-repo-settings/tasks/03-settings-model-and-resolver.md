# 03 Repository settings model, effective-settings resolver and cron helper

**Status:** todo
**Feature:** 004-per-repo-settings
**Depends on:** none

## What to do
Add the pure model and logic for layered settings and cron schedules. This task has no storage, API or UI, and changes nothing at runtime yet.

1. **`Domain/RepositorySettings.cs`** (new): a sealed record `RepositorySettings` with:
   - `bool InheritGlobal = true`
   - `IReadOnlyList<string> Patterns`, `Excludes`, `Feeds`, `Ignore`; null is normalised to empty, and entries are trimmed with blanks dropped
   - `UpdateStrategy? Strategy`, `bool? AllowPrerelease`, `string? Schedule` (blank is normalised to null)
   - `static RepositorySettings Inherit` (everything inherited)
   - `bool HasOverrides` (any list non-empty or any scalar non-null)
2. **`ManagedRepository`**: add `RepositorySettings Settings { get; private set; }` (default `RepositorySettings.Inherit`), an optional constructor parameter `RepositorySettings? settings = null`, and `void SetSettings(RepositorySettings settings)`.
3. **Global default schedule.** Add `string? DefaultSchedule` to `TargetingSettings` as an optional constructor parameter, placed last, with blank normalised to null.
4. **`Domain/EffectiveSettings.cs`** (new):
   - `enum SettingSource { Global, Repository, Both, Default }`
   - `sealed record EffectiveItem(string Value, SettingSource Source)`
   - `sealed record EffectiveValue<T>(T Value, SettingSource Source)`
   - `EffectiveSettings` with:
     - `TargetingSettings Targeting` (ready to hand to the planner, analyzer and verification)
     - `IReadOnlyList<EffectiveItem>` for each of patterns, excludes, feeds and ignore
     - `EffectiveValue<UpdateStrategy> Strategy`, `EffectiveValue<bool> AllowPrerelease`, `EffectiveValue<string> Schedule`
     - `bool InheritGlobal`
     - `IReadOnlyList<string> Warnings`
5. **`Domain/EffectiveSettingsResolver.cs`** (new): a static `Resolve(TargetingSettings global, RepositorySettings repo)`. The global default schedule comes from `global.DefaultSchedule`. Rules (copy them into the XML doc):
   - **Inheritance on:**
     - Each list is the global entries in order, then repo entries not already present (ordinal-ignore-case). An item in both lists has source `Both`; otherwise its source is `Global` or `Repository`.
     - Strategy and pre-release are the repo value (`Repository`) when non-null. Otherwise they are the global value (`Global`).
   - **Inheritance off:**
     - Lists contain only the repo's entries, all `Repository`.
     - Strategy and pre-release are the repo value (`Repository`) when non-null. Otherwise they are `UpdatePolicy.Default`'s value (`Default`).
   - **Schedule (either mode):** the repo value (`Repository`), else `global.DefaultSchedule` (`Global`), else `BuiltInDefaults.Schedule = "0 0 * * *"` (daily at 00:00 UTC, user decision) (`Default`). The schedule ignores `InheritGlobal` in both directions (confirmed by the user).
   - **Warnings:**
     - No effective patterns: "No package patterns: nothing will be updated for this repository."
     - Effective patterns but no feeds: "No feeds: package versions cannot be resolved."
6. **Cron helper** in `Infrastructure/Scheduling/CronSchedule.cs` (new). Add the `Cronos` package (latest stable) to `AutoRemediator.Infrastructure.csproj` only.
   - `static bool TryParse(string expression, out CronSchedule? schedule, out string? error)`:
     - uses `CronFormat.Standard` (5 fields; macros such as `@daily` are allowed)
     - rejects 6-field input with "Seconds are not supported; use 5 fields (minute hour day-of-month month day-of-week), UTC."
     - rejects expressions where any gap between consecutive occurrences in the next 100 (from a fixed reference instant, so it is deterministic) is less than `MinimumInterval = 15 minutes`, with "Schedules more frequent than every 15 minutes are not supported." (This floor is an assumption the user may revisit; keep the check in one place so it is easy to drop.)
   - `DateTimeOffset? NextAfter(DateTimeOffset utc)`: exclusive, UTC.
   - `IReadOnlyList<DateTimeOffset> Next(DateTimeOffset fromUtc, int count)`: for previews.
   - Expression text is kept as given, trimmed.

## Files
- `src/AutoRemediator.Domain/RepositorySettings.cs` (new), `EffectiveSettings.cs` (new), `EffectiveSettingsResolver.cs` (new)
- `src/AutoRemediator.Domain/ManagedRepository.cs`, `TargetingSettings.cs`
- `src/AutoRemediator.Infrastructure/Scheduling/CronSchedule.cs` (new), `AutoRemediator.Infrastructure.csproj` (Cronos)
- `tests/AutoRemediator.Domain.Tests/EffectiveSettingsResolverTests.cs` (new), `ManagedRepositoryTests.cs`
- `tests/AutoRemediator.Infrastructure.Tests/CronScheduleTests.cs` (new)

## Out of scope
- Persistence (task 04), consumers (task 05), API (task 06), and the scheduler (task 08).
- Validating that the strategy text is a known value. The domain uses the enum; parsing text is the API's job (task 06).

## Done when
- [ ] `EffectiveSettingsResolverTests` cover, at minimum:
  - all-inherit equals global (with `Global` sources)
  - a repo pattern added to a global one (union order, `Repository` source)
  - a duplicate in different case (one entry, `Both`, first spelling kept)
  - repo strategy wins
  - null strategy inherits
  - inheritance off with repo lists only and default scalars (`Default` source)
  - inheritance off where the schedule still comes from the global default
  - no schedule anywhere (built-in `0 0 * * *`, `Default`), with inheritance on and with inheritance off
  - inheritance off with no patterns (empty patterns plus the warning)
  - patterns without feeds (the feeds warning)
  - `Targeting` equals the effective lists and policy
- [ ] `CronScheduleTests` cover: a valid 5-field expression; `@daily`; garbage; 6 fields (the seconds message); `*/5 * * * *` (the minimum-interval message); `*/15 * * * *` (accepted); `NextAfter` being exclusive and UTC across a month boundary.
- [ ] `ManagedRepositoryTests`: a new repository has `RepositorySettings.Inherit`, and `SetSettings` replaces it.
- [ ] `Domain.csproj` still has no package references.
- [ ] `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass.

## Notes
- Use `PackagePatternMatcher`-style ordinal-ignore-case comparison. Package ids and globs are case-insensitive in NuGet.
- Keep the resolver free of I/O and of time. It is called in many places, and the API shows its output verbatim.
- Cronos: `CronExpression.Parse(expr, CronFormat.Standard)` throws `CronFormatException`. `GetNextOccurrence(DateTime utc)` needs `DateTimeKind.Utc`; the `DateTimeOffset` overload with `TimeZoneInfo.Utc` is simpler. Use `TryParse` if the chosen version has it.
