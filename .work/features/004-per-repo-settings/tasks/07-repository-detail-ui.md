# 07 Repository detail page and settings UI

**Status:** todo
**Feature:** 004-per-repo-settings
**Depends on:** 02, 06

## What to do
Add a repository detail page where an operator edits the repository and its settings, sees the effective settings with their sources, and can Run now. Extend the global settings form with the default schedule. This completes the "repositories can't be edited in the UI" fix, except for organization routing.

1. **Types and client** (`lib/types.ts`, `lib/api.ts`):
   - Mirror `RepositorySettings`, `EffectiveSettings`, `EffectiveListItem` and `EffectiveValue`.
   - Add `settings` to `ManagedRepository` and `ManagedRepositoryInput`, and `defaultSchedule` to `TargetingSettings`.
   - Add `api.repository(id)`, `api.repositorySettings(id)`, `api.saveRepositorySettings(id, s)` and `api.effectiveSettings(id)`.
   - Add a helper that reads a 400 `ValidationProblem` body into `Record<string, string>` (the first message per field).
2. **Route** `/configuration/repositories/:id` in `main.tsx`, rendering `pages/repository.tsx` (new):
   - **Header:** `PageHeader` titled with the repository name, the slug as the subtitle, and a Run now button (same behaviour and messages as task 02; reuse that logic by extracting a small hook, `useRunNow`, into `hooks.ts`).
     - Run now is available when the repository is disabled. In that case the header shows the visible text "Disabled: not scheduled. Run now still works." (`data-testid="repo-not-scheduled"`), referenced by the button's `aria-describedby`, and the queued message adds "will not run on schedule" as in task 02.
   - **Repository card:** a form for Organization, Project, Name, Branch and Enabled, saved with `PUT /api/repositories/{id}` **without** `settings`.
     - Organization has helper text (linked with `aria-describedby`): "Informational. All repositories use the configured Azure DevOps organization URL."
   - **Settings card** (`data-testid="repo-settings-form"`):
     - An "Inherit global settings" `Switch` (`data-testid="inherit-global"`). When it is off, an inline `Alert` explains: "This repository uses only the values below. Unset strategy and pre-release fall back to Minor and no pre-release. The schedule still falls back to the global default."
     - Comma-separated inputs for Patterns, Excludes, Feeds and Ignore. When inheriting, the helper text reads "Added to the global list".
     - Strategy as a `Select` with options `Inherit (global: <X>)` (or `Default (Minor)` when not inheriting), `Patch`, `Minor`, `Major`.
     - Allow pre-release as a `Select` with `Inherit (...)`, `Allow`, `Don't allow`.
     - Schedule as an `Input` with placeholder `default: <global or 0 0 * * *>` and help text "5-field cron, UTC, at most every 15 minutes". There is no occurrence preview in this task; task 09 adds the next runs to the effective view.
     - Saved with `PUT /api/repositories/{id}/settings`. Field errors from a 400 show under the field (`aria-invalid="true"`, `aria-describedby` pointing at the error, `data-testid="error-<field>"`).
   - **Effective settings card:** `DataTable` with caption "Effective settings" and columns Setting / Value / Source (`data-testid="effective-settings"`).
     - One row per scalar, and one row per list showing its items with a small source `Pill` each (`Global` / `Repo` / `Both` / `Default`).
     - Warnings render above the table as a warning `Alert` (`data-testid="effective-warnings"`).
     - It reloads after every save.
   - An unknown id shows the same not-found treatment as the run detail page for an unknown run.
3. **Configuration page:**
   - The name cell links to the detail page (`rowHref` plus `primaryColumnId` on the name column).
   - Add a "Settings" column with a `Pill`: `Inherits` (no overrides), `Overrides` (inheriting with overrides), `Local only` (inheritance off).
   - Add a "Schedule" column showing the repo's schedule or "default".
   - Rename the global card's heading to "Global settings" and add a "Default schedule (cron, UTC)" field (placeholder `0 0 * * *`, help text "Used by repositories without their own schedule. Empty means daily at 00:00 UTC.", `data-testid="default-schedule"`) with the same field-error handling.
4. **Home:** rename the "Targeting" card heading to "Global targeting", so it is not mistaken for what applies to every repository.
5. **Functional tests:**
   - Add `/configuration/repositories/{repo}` to `AccessibilityTests.Routes()` (resolve `{repo}` to the first id from `/api/repositories`, falling back to a random GUID as `{run}` does) and to `FocusVisibleTests.Cases()`.
   - New `RepositorySettingsTests`:
     - Set a repo-only pattern, save, and see it in the effective table with source Repo.
     - Turn inheritance off with no patterns and see the warning.
     - Enter `* * * * * *` and see the schedule error tied to the input (`aria-invalid`).
     - Restore the original settings at the end.

## Files
- `src/AutoRemediator.Web/src/lib/types.ts`, `src/lib/api.ts`, `src/hooks.ts`, `src/main.tsx`
- `src/AutoRemediator.Web/src/pages/repository.tsx` (new), `pages/configuration.tsx`, `pages/home.tsx`
- `tests/AutoRemediator.Web.FunctionalTests/AccessibilityTests.cs`, `FocusVisibleTests.cs`, a new `RepositorySettingsTests.cs`

## Out of scope
- Last and next scheduled run display (task 09).
- Organization routing.
- New npm dependencies. Do not add a cron parser to the SPA; the server validates and previews.

## Done when
- [ ] `npm run lint && npm run typecheck && npm run build` pass.
- [ ] With a running app (`WEB_BASE_URL` set): `AccessibilityTests` pass for every route (including the new one) in light and dark, and `FocusVisibleTests` pass for `/configuration` and the new route in light, dark and forced-colours. `RepositorySettingsTests` and `RunNowTests` pass.
- [ ] Every interactive element on the new page uses `focusRing` (through the shared primitives), every table has a caption, and every field has a visible `Label`.
- [ ] Manually: editing a repository's branch and saving keeps its per-repo settings. Saving settings keeps the branch.
- [ ] Manually: on `disabled-repo`'s page, Run now is available, "Disabled: not scheduled" is visible, and the run queues.
- [ ] With no global default schedule, the repository page's Schedule row shows `0 0 * * *` with source `Default`.

## Notes
- **Next occurrences.** This task shows only the effective schedule and its source. Task 09 adds the last and next runs from the server. Do not add a client-side cron parser.
- **Tri-state selects.** Radix `Select` cannot hold an empty-string value. Use sentinel values such as `inherit`, and map them to `null` in the payload.
- Follow the comma-split pattern (`split`) from `configuration.tsx` for list fields, and move it to `lib/` for reuse.
