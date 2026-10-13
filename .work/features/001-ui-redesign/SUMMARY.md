# UI redesign: modern, sleek, WCAG 2.2 AA — Summary

**Completed:** 2026-10-13

## What changed
The React SPA in `src/AutoRemediator.Web` got a quieter, more modern look and now meets WCAG 2.2 AA in both light and dark themes. Design tokens were reworked first (contrast fixes, a 12px text floor, 14px body, Inter variable font, card radius and elevation), followed by a theme control with `light | dark | system` modes that follows the OS by default and applies before first paint. The shell gained a skip link, proper landmarks, `aria-current` navigation, a Radix Dialog drawer on mobile, and focus moved to the page heading on route change. Shared components and `DataTable` were made keyboard-operable (one row link per row as the only tab stop), and pages were polished. An axe check now runs against every route in both themes, and the functional tests pass 18/18. A human verified the result manually.

## Tasks
- 01 Design tokens, theme modes and type: contrast fixes and a measured contrast table in `index.css`, 12px floor, Inter, `useTheme` with `system` default and no flash on load.
- 02 Accessible shell and navigation: skip link, `header` / `nav aria-label="Primary"` / `main`, `aria-expanded` toggle, Radix Dialog drawer below `md`, focus to `h1` on pathname change, global reduced-motion rule.
- 03 Shared components and keyboard-operable tables: shared focus ring, restyled controls with `border-input`, pill contrast, `DataTable` row-link API with `caption` and `scope="col"`, `onRowClick` removed.
- 04 Page polish and accessibility verification: page spacing and headings, mono limited to identifiers, labelled config fields and live status messages, `AccessibilityTests.cs` (axe over every route x theme, plus the mobile drawer), manual checklist in the README.

## Files touched
- `src/AutoRemediator.Web/`: `index.html`, `package.json`, `package-lock.json`, `src/index.css`, `src/main.tsx`, `src/hooks.ts`, `src/lib/utils.ts`, `src/components/` (shell, header, table, pill, cells, new `ui/sheet.tsx`, restyled `ui/` primitives), `src/pages/`.
- `tests/AutoRemediator.Web.FunctionalTests/`: `AccessibilityTests.cs` (new), `HomePageSmokeTests.cs`, csproj (`Deque.AxeCore.Playwright`).
- `README.md`: axe test notes and the manual accessibility checklist.

## Follow-ups
- The theme control cycles system, light, dark; a menu may read more clearly later.
- axe uses the C# port `Deque.AxeCore.Playwright` (same engine as the npm package); revisit only if a Node test runner is wanted.
- Table scroll regions are always focusable; conditional focus would need overflow detection.
- Deferred: charts, custom logo, density toggle, i18n, extra motion design.
