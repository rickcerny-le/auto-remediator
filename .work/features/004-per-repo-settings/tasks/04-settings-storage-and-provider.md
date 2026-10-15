# 04 Settings storage and effective-settings provider

**Status:** done
**Feature:** 004-per-repo-settings
**Depends on:** 03

## What to do
Persist per-repository settings and the global default schedule backward-compatibly, and add the single place that produces a repository's effective settings.

1. **`ManagedRepositoryEntity`** (`Configuration/ManagedRepositoryStore.cs`):
   - Add nullable columns: `InheritGlobal` (`bool?`, null = true), `PatternsJson`, `ExcludesJson`, `FeedsJson`, `IgnoreJson` (`string?`, null or blank = empty), `Strategy` (`string?`; null or unparseable = null, i.e. inherit), `AllowPrerelease` (`bool?`) and `Schedule` (`string?`).
   - `FromDomain` writes them. To keep rows lean, it writes **null** for an empty list and null scalars, so an inherit-everything repository looks exactly like a legacy row.
   - `ToDomain` builds `RepositorySettings` from them.
2. **`TargetingSettingsEntity`** (`Configuration/TargetingSettingsStore.cs`): add `DefaultSchedule` (`string?`), and read and write it into `TargetingSettings.DefaultSchedule`.
3. **Provider.** Add `Configuration/EffectiveSettingsProvider.cs`:
   - Interface `IEffectiveSettingsProvider` with `Task<EffectiveSettings> GetAsync(ManagedRepository repository, CancellationToken ct)`. It loads the global settings from `ITargetingSettingsStore` and returns `EffectiveSettingsResolver.Resolve(global, repository.Settings)`.
   - Add an overload `GetAsync(IEnumerable<ManagedRepository>, ct)` returning a dictionary by id. It loads the global settings once, for the dependency map and the scheduler.
   - Register it as a singleton.
4. **Test doubles.** Add an in-memory provider or reuse `InMemorySettingsStore` in `tests/AutoRemediator.Infrastructure.Tests/InMemoryStores.cs`, so tasks 05 and 08 can use it.

## Files
- `src/AutoRemediator.Infrastructure/Configuration/ManagedRepositoryStore.cs`, `TargetingSettingsStore.cs`
- `src/AutoRemediator.Infrastructure/Configuration/EffectiveSettingsProvider.cs` (new)
- `src/AutoRemediator.Infrastructure/InfrastructureExtensions.cs`
- `tests/AutoRemediator.Infrastructure.Tests/ConfigStoreRoundTripTests.cs`, `InMemoryStores.cs`, a new `EffectiveSettingsProviderTests.cs`

## Out of scope
- Changing any consumer to use the provider (task 05).
- The schedule state table (task 08).
- API changes (task 06).

## Done when
- [ ] Round-trip tests (Azurite-backed, self-skipping like the existing `ConfigStoreRoundTripTests`):
  - A repository with every per-repo field set reads back equal.
  - An inherit-everything repository writes no settings columns (null).
  - Global settings with and without `DefaultSchedule` round-trip.
- [ ] **Legacy rows.** A test inserts a raw `TableEntity` shaped like a pre-feature repository row (only `Organization`, `Project`, `Name`, `Enabled`, `TargetBranch`) and a pre-feature global settings row, then reads them through the stores. The repository has `RepositorySettings.Inherit`, and the global settings have `DefaultSchedule == null`.
- [ ] `EffectiveSettingsProviderTests`: the provider combines the stored global settings with the repository's settings (one inherit case, one override case), and the batch overload reads the global settings once.
- [ ] `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass.

## Notes
- The stores upsert with `TableUpdateMode.Replace`, so every column must be written on every save. That is why nulls, not "missing", mean "inherit".
- Use the same `JsonSerializer` list helpers as `TableTargetingSettingsStore` (`Serialize` / `Deserialize`). Consider moving them to a small internal static helper shared by both stores.
