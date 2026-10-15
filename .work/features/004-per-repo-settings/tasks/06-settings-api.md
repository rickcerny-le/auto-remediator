# 06 API for repository settings, effective settings and default schedule

**Status:** done
**Feature:** 004-per-repo-settings
**Depends on:** 05

## What to do
Expose per-repository settings, the effective view and the global default schedule over HTTP, with validation. All contract changes are additive, and new optional fields go last.

1. **Contracts** (`Contracts/Dtos/ConfigurationDtos.cs`):
   - New `RepositorySettingsDto(bool InheritGlobal, IReadOnlyList<string> Patterns, IReadOnlyList<string> Excludes, IReadOnlyList<string> Feeds, IReadOnlyList<string> Ignore, string? Strategy, bool? AllowPrerelease, string? Schedule)`.
   - Append `RepositorySettingsDto? Settings = null` to `ManagedRepositoryDto` and to `ManagedRepositoryInput`.
   - Append `string? DefaultSchedule = null` to `TargetingSettingsDto`.
   - New `EffectiveListItemDto(string Value, string Source)`, `EffectiveValueDto(string Value, string Source)`, and `EffectiveSettingsDto(Guid RepositoryId, bool InheritGlobal, IReadOnlyList<EffectiveListItemDto> Patterns, IReadOnlyList<EffectiveListItemDto> Excludes, IReadOnlyList<EffectiveListItemDto> Feeds, IReadOnlyList<EffectiveListItemDto> Ignore, EffectiveValueDto Strategy, EffectiveValueDto AllowPrerelease, EffectiveValueDto Schedule, IReadOnlyList<string> Warnings)`.
   - Source strings are the `SettingSource` names. `AllowPrerelease.Value` is `"true"` or `"false"`.
2. **Validation helper** (`Api/Features/Repositories/SettingsValidation.cs`, or a shared spot under `Features`). It returns a `Dictionary<string, string[]>` keyed by camelCase field name (`strategy`, `schedule`, `defaultSchedule`, `patterns`):
   - `strategy`: null, or `Patch` / `Minor` / `Major` (case-insensitive).
   - `schedule` / `defaultSchedule`: null or blank, or `CronSchedule.TryParse` succeeds (its error text is the message).
   - List entries: trimmed and non-empty, at most 200 characters, and no `,` inside an entry (the UI splits on commas).
3. **Repositories endpoints:**
   - `GET /api/repositories/{id}`: the DTO, or 404.
   - `GET /api/repositories`: each DTO now includes `Settings`.
   - `POST`: `input.Settings` null means `RepositorySettings.Inherit`; otherwise validate it (400 `Results.ValidationProblem`).
   - `PUT /{id}`: **null `Settings` keeps the stored settings**. A non-null value is validated and replaces them. The current code rebuilds the entity from the input, so load the existing entity first and carry its settings over.
   - `GET /api/repositories/{id}/settings` and `PUT /api/repositories/{id}/settings` (body `RepositorySettingsDto`): 404 for an unknown repository, 400 on validation errors, 200 with the saved DTO.
   - `GET /api/repositories/{id}/effective-settings`: maps `IEffectiveSettingsProvider.GetAsync(repo)` to `EffectiveSettingsDto`; 404 for an unknown repository.
4. **Settings endpoint** (`Features/Settings/SettingsEndpoints.cs`):
   - Read and write `DefaultSchedule`.
   - `PUT` validates `strategy` and `defaultSchedule` and returns 400 `ValidationProblem` on error. Today an unknown strategy silently becomes Minor; that coercion is removed.
5. **Tests** (`tests/AutoRemediator.Api.Tests`): follow `RepositoriesEndpointTests` (in-memory stores swapped into `ApiFactory`). Use an in-memory `ITargetingSettingsStore` so the real provider can run.

## Files
- `src/AutoRemediator.Contracts/Dtos/ConfigurationDtos.cs`
- `src/AutoRemediator.Api/Features/Repositories/RepositoriesEndpoints.cs`, `SettingsValidation.cs` (new)
- `src/AutoRemediator.Api/Features/Settings/SettingsEndpoints.cs`
- `tests/AutoRemediator.Api.Tests/RepositoriesEndpointTests.cs`, a new `RepositorySettingsEndpointTests.cs`, a new `SettingsEndpointTests.cs`

## Out of scope
- Last and next scheduled times on the effective view (task 09 appends them).
- UI (task 07).
- Per-repository organization routing (feature-level out of scope).

## Done when
- [ ] API tests:
  - `PUT /settings` round-trips every field.
  - An invalid cron returns 400 with an `errors.schedule` entry.
  - A 6-field cron returns 400 with the seconds message.
  - `*/5 * * * *` returns 400 with the minimum-interval message.
  - An unknown strategy returns 400 with `errors.strategy`.
  - `PUT /api/repositories/{id}` without `settings` keeps previously saved settings, and with `settings` replaces them.
  - `POST` with `settings` stores them.
  - `GET /effective-settings` returns the union with sources for an inherit case and an inheritance-off case, plus the "No package patterns" warning when the repository has inheritance off and no patterns.
  - `PUT /api/settings` with `defaultSchedule` round-trips, and an unknown strategy returns 400.
  - Unknown ids return 404 on every new route.
- [ ] Old clients still work. A JSON body without `settings` or `defaultSchedule` (as `Set-LocalScenarioConfig.ps1` sends today) is accepted on `POST`/`PUT` repositories and `PUT /settings`.
- [ ] `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass. Rerun once before treating a `WebApplicationFactory` host timeout as a failure (known flaky suite).

## Notes
- `Results.ValidationProblem(errors)` produces RFC 7807 with an `errors` object. The UI (task 07) reads `errors[field][0]`.
- Keep endpoints thin: mapping helpers (`ToDto`, `FromDto`, `ToEffectiveDto`) as private static methods in the endpoint class, as the existing slices do.
