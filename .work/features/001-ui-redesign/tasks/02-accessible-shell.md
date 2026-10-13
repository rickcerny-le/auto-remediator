# 02 Accessible shell and navigation

**Status:** done
**Feature:** ui-redesign
**Depends on:** 01

## What to do
Restyle and fix the semantics of the app shell.

1. **Skip link**: first focusable element in the page, "Skip to main content", visually hidden until focused, targets `<main id="main" tabIndex={-1}>`.
2. **Landmarks**: top bar is `<header>`; nav is `<nav aria-label="Primary">`; content is `<main>`. Exactly one of each.
3. **Nav links**: `NavLink` already sets `aria-current="page"` when active; style the active item from that attribute (e.g. `aria-[current=page]:...`), not colour alone: keep a visible indicator such as the left bar plus weight change. Icons are `aria-hidden`.
4. **Drawer toggle**: `aria-expanded` and `aria-controls` pointing at the nav.
5. **Layout**: at `md` and up, the sidebar is a persistent, collapsible column (current behaviour). Below `md`, render the nav inside a Radix Dialog (from `radix-ui`, add a shadcn-style `src/components/ui/sheet.tsx` or `dialog.tsx`) as a left drawer: focus trapped, Esc and overlay click close it, focus returns to the toggle, closes on navigation. The `data-testid="nav"` must stay on the visible `<nav>` element at both widths.
6. **Slimmer top bar** (e.g. 48px) using tokens from task 01; brand mark gets `aria-hidden` on the glyph; the "test" environment badge stays readable (>= 12px, >= 4.5:1).
7. **Focus on route change**: in `page-header.tsx`, the `h1` gets `tabIndex={-1}` and is focused when the route's pathname changes (use `useLocation` in `PageHeader` or a small hook). Do not steal focus on the very first load (keep the skip link as the first Tab stop); do not move focus when only search params change (e.g. the Runs status filter). Hide the outline for programmatic focus on the heading only if it stays clear which element is focused; do not hide focus rings elsewhere.
8. **Global reduced motion**: one rule in `index.css` under `@media (prefers-reduced-motion: reduce)` that sets transitions and animations to effectively none for all elements (this replaces the pill-only rule).

## Files
- `src/AutoRemediator.Web/src/components/app-shell.tsx` — skip link, landmarks, aria attrs, desktop sidebar vs mobile Dialog drawer, slimmer bar
- `src/AutoRemediator.Web/src/components/ui/sheet.tsx` (new) — Radix Dialog drawer primitive
- `src/AutoRemediator.Web/src/components/page-header.tsx` — focus `h1` on pathname change
- `src/AutoRemediator.Web/src/index.css` — skip-link style if not done with utilities, global reduced-motion rule

## Out of scope
- Restyling buttons/inputs/cards/tables (task 03).
- Page content layout (task 04).

## Done when
- [ ] First Tab on any page shows the skip link; Enter moves focus to `main`.
- [ ] Nav has `aria-label="Primary"`; the active link has `aria-current="page"` and a non-colour indicator.
- [ ] Toggle button exposes `aria-expanded` that matches drawer state.
- [ ] At 375px width: the drawer opens as a dialog, Tab cycles inside it, Esc closes it, focus returns to the toggle, following a link closes it.
- [ ] Clicking a nav link (or any in-app link) moves focus to that page's `h1`; changing the Runs status filter does not move focus.
- [ ] With reduced motion emulated (DevTools rendering panel), the running pill dot does not pulse and no transitions play.
- [ ] `npm run lint && npm run typecheck && npm run build` pass; `HomePageSmokeTests` and `ReviewGateTests` pass (both use `nav` and `theme-toggle`).

## Notes
- The current drawer is rendered conditionally with `drawerOpen` initialised from `isWide()`; keep the desktop collapse behaviour but give mobile its own Dialog state so the desktop sidebar is not a modal.
- `HomePageSmokeTests` runs at 1440x900 and expects `nav` visible without clicking anything; keep the sidebar open by default at that width.
- The Dialog needs an accessible title (`Dialog.Title`, visually hidden is fine, e.g. "Navigation").
