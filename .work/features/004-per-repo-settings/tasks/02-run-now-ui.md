# 02 Run now, enable switch and Manual badge (UI)

**Status:** todo
**Feature:** 004-per-repo-settings
**Depends on:** 01

## What to do
Make the repository table on the Configuration page actionable, and show the run trigger in the runs UI. This fixes the "repositories can't be enabled/disabled in the UI" half of the PRD known defect.

1. **Types and client** (`lib/types.ts`, `lib/api.ts`):
   - Add `trigger: 'Scheduled' | 'Manual'` to `RunSummary` and `RunDetail`.
   - Add `api.updateRepository(id, input)` (`PUT /api/repositories/{id}`).
   - Add `api.runNow(id)`, returning `{ status, body }`, where the body is `{ runId, enabled }` on 202 and `{ reason, heldRunId?, message }` on 409.
2. **Enabled switch.** Replace the "yes"/"no" text in the Enabled column with the shared `Switch`:
   - Accessible name: `aria-label="Enable <name>"`; `data-testid="repo-enabled"`.
   - Toggling it PUTs the repository with the flipped `enabled` and every other field unchanged, then reloads the list. The switch is disabled while the request is in flight.
   - Report failures in the existing `config-status` alert.
3. **Run now button.** Add a column with a `Button` (`variant="outline"`, `size="sm"`, text "Run now", `aria-label="Run <name> now"`, `data-testid="run-now"`):
   - It is disabled while its request is pending.
   - **It is offered on disabled repositories too** (user decision: useful for trying a repository before enabling it). For a disabled repository the cell also shows muted visible text "Disabled: not scheduled" (`data-testid="repo-not-scheduled"`) next to the button, and the button references that text with `aria-describedby`, so screen-reader users hear it too. The state is conveyed in text, not by colour or the switch alone.
   - **202:** the status alert says "Run queued for <name>. It appears in Runs when the worker picks it up." with a link to `/runs`. When the response has `enabled: false`, append "<name> is disabled and will not run on schedule."
   - **409 held:** "<name> is held for review; resolve the held run first." with a link to `/runs/<heldRunId>`.
   - Any other error: the message from the API in the same alert.
4. **Manual badge.** In the runs feed (`pages/runs.tsx`, through `components/run-cells.tsx` if that is where status cells are built), on Home's recent runs, and on the run detail header (`pages/run-detail.tsx`), show a `Pill` reading "Manual" (`data-testid="trigger-manual"`) next to the status when `trigger === 'Manual'`. Scheduled runs show nothing. Pick a tone with at least 4.5:1 text contrast in both themes (reuse an existing `Pill` tone; do not add colours).
5. **Functional test.** Add `RunNowTests` to `tests/AutoRemediator.Web.FunctionalTests` (skipped without `WEB_BASE_URL`, like the others):
   - On `/configuration`, pressing Run now on the first enabled repository shows the queued status.
   - Toggling a repository's switch changes its stored `enabled` (read back through `/api/repositories`), then the test toggles it back.
   - On a disabled repository (disable one through the API first, then restore it), the Run now button is present and enabled, the "Disabled: not scheduled" text is visible, and pressing it shows the queued status including "will not run on schedule".

## Files
- `src/AutoRemediator.Web/src/lib/types.ts`, `src/lib/api.ts`
- `src/AutoRemediator.Web/src/pages/configuration.tsx`
- `src/AutoRemediator.Web/src/pages/runs.tsx`, `pages/home.tsx`, `pages/run-detail.tsx`, `components/run-cells.tsx`
- `tests/AutoRemediator.Web.FunctionalTests/RunNowTests.cs` (new)

## Out of scope
- Editing repository fields and per-repo settings (task 07).
- The repository detail page (task 07). The name cell does not link anywhere yet.
- A Run now button on Home.

## Done when
- [ ] `npm run lint && npm run typecheck && npm run build` pass in `src/AutoRemediator.Web`.
- [ ] Every new interactive element uses `focusRing` (the shared `Button`/`Switch` already do; do not override `outline`). `FocusVisibleTests` for `/configuration` pass in light, dark and forced-colours with a running app.
- [ ] `AccessibilityTests` for `/configuration`, `/runs` and `/runs/{run}` pass in light and dark with a running app. At least one manual run must exist so the badge is exercised; trigger one with Run now first.
- [ ] `RunNowTests` passes against a running app (local mode is fine).
- [ ] Manually: Run now on a held repository (seed `held-review` with `held-run-seeder` in local mode) shows the held message with a working link. Run now on `disabled-repo` queues a run (it appears in Runs with the `Manual` badge), the row says "Disabled: not scheduled", the status message says it will not run on schedule, and the repository is still disabled afterwards.

## Notes
- Read `.work/agents/frontend.md`: preserve each file's line endings, and use the `.cjs`-with-node trick for scripted multi-line edits.
- The enable switch and the "Disabled: not scheduled" text must agree immediately after a toggle (both come from the reloaded list).
- `DataTable` rows have no row click when `rowHref` is unset, so buttons and switches inside cells do not conflict with navigation.
- The `Switch` in `components/ui/switch.tsx` is Radix-based. Give it an `aria-label`, because there is no visible label in a table cell.
