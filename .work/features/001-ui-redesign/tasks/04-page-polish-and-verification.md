# 04 Page polish and accessibility verification

**Status:** done
**Feature:** ui-redesign
**Depends on:** 02, 03

## What to do
Polish each page with the new tokens and components, then verify the whole feature.

### Page polish
For Home, Runs, Run detail, Dependency map, Configuration (and Not found):
- More whitespace between sections; consistent section headings (`h2` under the page `h1`, no skipped levels).
- Home KPIs: larger value type from the task 01 scale; KPI values use the sans font with tabular numerals, not mono.
- Mono only for repository slugs, package names/versions, hashes and file paths. Remove it from timestamps, counts and subtitles unless they are identifiers.
- Replace remaining hard-coded sizes and colours in pages (e.g. `text-[13px]` in `home.tsx`, `text-[#0d1117]` in the brand mark if still present) with tokens.
- External links (`target="_blank"`) indicate they open in a new tab (visually hidden text or an icon with a label).
- Form fields on Configuration have visible labels associated with their inputs; error/status messages (`config-status`, `review-command-message`) are announced (`role="status"` or `aria-live="polite"`).
- Layout reflows at 320px with no horizontal page scroll; wide tables scroll inside their own container (with `tabIndex={0}` and a label on the scroll container if it can scroll).

### Automated verification (axe)
- Add `Deque.AxeCore.Playwright` (NuGet) to `tests/AutoRemediator.Web.FunctionalTests` (see Risks in FEATURE.md: `@axe-core/playwright` is Node-only and cannot run in this C# project; confirm with the user).
- New test class `AccessibilityTests.cs`, skipped unless `WEB_BASE_URL` is set (same pattern as `HomePageSmokeTests`). A theory over routes `/`, `/runs`, `/runs/{id}` (use an existing run from `/api` or the unknown-run page if none exists), `/dependency-map`, `/configuration`, and an unknown path (Not found), crossed with `ColorScheme.Light` and `ColorScheme.Dark`.
- Wait for each page's content to load (its `h1`, and loading text gone) before running axe, restricted to tags `wcag2a, wcag2aa, wcag21a, wcag21aa, wcag22aa`. Fail with the rule IDs and target selectors in the message.
- Also run axe once at 375px width with the mobile drawer open.

### Manual checklist
Add a short checklist section to the README "Functional tests" area (or `docs/`) and record the result in this task file's Notes when done:
- Keyboard only: every page reachable and operable, focus visible, no traps outside the drawer.
- 200% browser zoom at 1280px: no clipped or overlapping content.
- 320px width: no horizontal scroll except inside tables.
- Text spacing override (WCAG 1.4.12 bookmarklet): no clipped text.
- Reduced motion on: nothing animates.
- Windows high contrast / forced colors: focus rings and pill labels remain visible.

## Files
- `src/AutoRemediator.Web/src/pages/home.tsx`, `runs.tsx`, `run-detail.tsx`, `dependency-map.tsx`, `configuration.tsx`, `not-found.tsx` — spacing, headings, type, link labelling, live regions
- `tests/AutoRemediator.Web.FunctionalTests/AutoRemediator.Web.FunctionalTests.csproj` — `Deque.AxeCore.Playwright` reference
- `tests/AutoRemediator.Web.FunctionalTests/AccessibilityTests.cs` (new) — axe over every route in both themes
- `README.md` — manual a11y checklist and how to run the axe tests

## Out of scope
- Charts, custom logo, density toggle, i18n, extra animation.
- New page features or API changes.
- CI wiring for functional tests (they remain opt-in via `WEB_BASE_URL`).

## Done when
- [ ] `AccessibilityTests` passes against a running app with zero violations for every route x theme combination, plus the mobile drawer check.
- [ ] `HomePageSmokeTests` and `ReviewGateTests` pass unchanged (apart from the task 01 colour-scheme change).
- [ ] Every `data-testid` listed in FEATURE.md "Test hooks" still exists (`grep` each in `src`).
- [ ] No `text-[<n>px]` arbitrary sizes and no hex colours remain in `src/pages` (`grep -rnE "text-\[[0-9.]+px\]|#[0-9a-fA-F]{3,6}" src/pages` is empty).
- [ ] Manual checklist completed and results recorded in this file's Notes.
- [ ] `npm run lint && npm run typecheck && npm run build` pass.

## Notes
- Functional tests: `pwsh tests/AutoRemediator.Web.FunctionalTests/bin/Debug/net10.0/playwright.ps1 install`, then `WEB_BASE_URL=<web-url> dotnet test tests/AutoRemediator.Web.FunctionalTests`. Start the app with `aspire start` (not `dotnet run` on the AppHost).
- `ReviewGateTests` seeds an AwaitingReview run; reuse its approach if the accessibility test needs a real run id, or keep it simple and use the first run returned by the API, skipping the run-detail case when none exists.
- If this task grows too large in review, split page polish from verification.
