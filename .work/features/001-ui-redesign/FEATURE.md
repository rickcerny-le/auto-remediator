# UI redesign: modern, sleek, WCAG 2.2 AA

**Status:** done
**Created:** 2026-10-13

## Goal
Give the React SPA (`src/AutoRemediator.Web`) a quieter, more modern look and bring it to WCAG 2.2 AA in both light and dark themes. Measured audit findings (below) show contrast, keyboard, and navigation-semantics gaps that a restyle alone would not fix. The redesign is deliberately small: tokens first, then shell, then shared components, then page polish plus automated (axe) and manual verification.

## Scope
**In:**
- Design tokens in `src/index.css`: contrast fixes, type scale with a 12px floor and 14px body, 12px card radius, elevation tokens, self-hosted Inter variable font.
- Theme modes `light | dark | system`, defaulting to `system`, applied before first paint by the inline script in `index.html`.
- Shell: skip link, landmarks, nav `aria-label`/`aria-current`, toggle `aria-expanded`, mobile drawer as a Radix Dialog, focus moved to the page `h1` on route change, global `prefers-reduced-motion`.
- Shared components: button, input, card, select, switch, accordion, pill, DataTable restyle; keyboard-operable table rows via a real link in the primary cell.
- Page polish for Home, Runs, Run detail, Dependency map, Configuration (and Not found by inheritance).
- An axe-core check in `tests/AutoRemediator.Web.FunctionalTests` against every route in both themes, plus a manual checklist.

**Out:** charts, a custom logo, a density toggle, i18n, extra animation or motion design. Backend/API changes. New pages or features.

## Acceptance criteria
- [ ] All text meets 4.5:1 (normal) or 3:1 (large) in both themes; control boundaries, focus indicators and the pill dot/text meet 1.4.11 (3:1). A contrast table in `index.css` documents the measured ratios.
- [ ] No text renders below 12px; body text is 14px.
- [ ] Theme follows the OS by default, can be forced to light or dark, persists in `localStorage`, and never flashes the wrong theme on load.
- [ ] A keyboard-only user can: skip to main content, operate the nav and the mobile drawer (focus trapped, Esc closes, focus returns to the toggle), open any run from any table, and land on the page `h1` after each route change.
- [ ] Every interactive element shows a visible focus ring (2px, 2px offset, at least 3:1 against its background).
- [ ] With `prefers-reduced-motion: reduce`, no transitions or animations run.
- [ ] axe-core reports zero violations at WCAG 2.0/2.1/2.2 A and AA tags on every route, in both themes.
- [ ] Pages reflow at 320px CSS width and stay usable at 200% zoom (manual checklist recorded in task 04).
- [ ] Every existing `data-testid` hook still exists and the existing functional tests (`HomePageSmokeTests`, `ReviewGateTests`) pass.
- [ ] `npm run lint && npm run typecheck && npm run build` pass in `src/AutoRemediator.Web`.

## Context
**Stack:** Vite + React 19, React Router 8 (`createBrowserRouter` in `src/main.tsx`), Tailwind v4 (CSS-first config, `@theme inline` in `src/index.css`), shadcn/ui primitives in `src/components/ui/` built on the `radix-ui` umbrella package (Dialog is available from it without a new dependency), lucide icons, TanStack Table v9 (`src/lib/table.ts`).

**Key files:**
- `index.html` — already has an inline pre-paint script (`localStorage.theme === 'light'` removes `dark`). `<html class="dark">` is hard-coded today. No CSP is set in `nginx.conf.template`, so an inline script is fine.
- `src/index.css` — all tokens for `:root` (light) and `.dark`; `ar-label`, `ar-pill`, `ar-tag` component classes; the only `prefers-reduced-motion` rule today.
- `src/components/app-shell.tsx` — top bar, theme toggle (`useDarkMode`, boolean), nav drawer rendered conditionally (`drawerOpen`), `<main>`.
- `src/components/page-header.tsx` — the `h1` every page renders.
- `src/components/data-table.tsx` — `onRowClick` on `<tr>`; used with `onRowClick` by `pages/home.tsx` (held + recent tables) and `pages/runs.tsx`.
- `src/components/pill.tsx`, `src/components/run-cells.tsx`, `src/lib/status.ts` — status tones and labels.
- `tests/AutoRemediator.Web.FunctionalTests/` — C# xUnit v3 + Microsoft.Playwright (.NET). Skipped unless `WEB_BASE_URL` is set (see README "Functional tests").

**Measured findings this feature fixes:**
- Borders ~1.4:1 light, 1.4-1.55:1 dark; inputs/controls need 3:1 (WCAG 1.4.11).
- `--subtle` text 3.46:1 light, 4.12:1 dark; needs 4.5:1. Used by the "—" placeholder in `run-cells.tsx`.
- `ar-label` 11px, `ar-pill` 11.5px, `ar-tag` 10.5px; raise to a 12px floor.
- DataTable rows are click-only (2.1.1, 4.1.2).
- No skip link; nav lacks `aria-label`/`aria-current`; drawer toggle lacks `aria-expanded`; no focus move on route change.
- Only the pill pulse honors `prefers-reduced-motion`.
- Pill tinted-text-on-tinted-background contrast never verified.
- Theme ignores OS preference.

**Decisions made with the user (log in decisions.md):**
- Theme defaults to `system`; modes are `light | dark | system`; no flash on load via the pre-paint script.
- Inter is acceptable, self-hosted as a variable font (no external CDN).
- axe-core runs in `tests/AutoRemediator.Web.FunctionalTests` against every route in both themes.
- Keep it simple; expand later.

**Design direction:** quieter surfaces, more whitespace, 12px card radius, subtle elevation (shadow in light, one-step-lighter surface in dark), larger KPI type, mono only for slugs/versions/hashes, keep the emerald brand and the ok/warn/bad/info semantic tones, 2px-offset focus rings at 3:1, slimmer top bar, sidebar with clear `aria-current` styling, mobile drawer as a Radix Dialog.

**Test hooks:** these `data-testid`s are used by tests and must survive unchanged: `nav`, `theme-toggle`, `refresh-runs`, `runs-table`, `run-error`, `review-panel`, `proposal-diagnostics`, `proposal-file`, `proposal-transcript-link`, `approve-button`, `retry-button`, `discard-button`, `review-command-message`, `review-note`, `rebuild-button`, `run-error-message`, `held-panel`, `held-table`. Keep every other existing `data-testid` too.

## Tasks
1. [todo] 01 Design tokens, theme modes and type
2. [todo] 02 Accessible shell and navigation
3. [todo] 03 Shared components and keyboard-operable tables
4. [todo] 04 Page polish and accessibility verification

## Risks and open questions
- **axe package for .NET tests.** `@axe-core/playwright` is a Node package and cannot be used from the C# Playwright tests in `tests/AutoRemediator.Web.FunctionalTests`. The plan uses the Deque-maintained NuGet port `Deque.AxeCore.Playwright` (same axe-core engine, same rule tags) in that project instead. If the user specifically wants the npm package, it would need a separate Node Playwright test runner, which is a bigger change. Needs confirmation.
- **Smoke test asserts "dark by default".** `HomePageSmokeTests` expects `html.dark` on first load. With `system` as default, Playwright's default emulated scheme (light) would fail it. Task 01 updates the test to emulate `ColorScheme.Dark` and keeps the toggle behavior it checks working (cycle order chosen so the existing two clicks still go dark -> light -> dark).
- **Theme control shape.** A three-state control could be a cycling icon button or a small menu. The plan uses a cycling button (system -> light -> dark) to stay simple and keep `theme-toggle` a single clickable element; its accessible name states the current mode. Switch to a menu later if users find cycling unclear.
- **Exact replacement colors** are left to task 01 with measured ratios recorded in the contrast table; brand emerald may need a slightly darker light-mode shade for text on tinted pill backgrounds.
- **Directory naming.** No `.work` existed before this feature; the folder is `ui-redesign` as requested rather than the numbered `001-ui-redesign` form. Rename if numbering is wanted.
