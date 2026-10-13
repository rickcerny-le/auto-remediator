# 01 Design tokens, theme modes and type

**Status:** done
**Feature:** ui-redesign
**Depends on:** none

## What to do
Rework the design foundation so later tasks only consume tokens.

1. **Contrast fixes in `src/index.css`** (both `:root` and `.dark`):
   - Split boundaries into two roles: `--border` (decorative dividers, card outlines; no ratio required) and `--input` (control boundaries for inputs, select triggers, switch track, outline buttons; must be >= 3:1 against `--background` and `--card`). Today both are `#d0d7de` / `#30363d` (~1.4-1.55:1).
   - Raise `--subtle` to >= 4.5:1 against `--background`, `--card` and `--muted` (today 3.46:1 light, 4.12:1 dark).
   - Make `--ring` >= 3:1 against `--background` and `--card` in both themes.
   - Check `--muted-foreground`, `--primary` on `--primary-foreground`, `--info` (used for link text), `--warning`, `--destructive`, `--success` as text on `--background` and `--card`: all >= 4.5:1.
2. **Type, radius, elevation tokens** in `@theme inline`: body 14px, a 12px minimum for small/label text, a larger KPI size (e.g. `text-4xl` equivalent), `--radius: 12px` for cards (keep controls around 8px via `--radius-md`), and `--shadow-card` (soft shadow in light; `none` in dark, where elevation is a one-step-lighter `--card` surface). Raise `ar-label` (11px), `ar-pill` (11.5px) and `ar-tag` (10.5px) to at least 12px.
3. **Inter**: add `@fontsource-variable/inter` as a dependency, import it once (from `main.tsx` or `index.css`), and set it as `--font-sans` with the current system stack as fallback. Keep `--font-mono` as is. No CDN requests.
4. **Theme modes `light | dark | system`, default `system`:**
   - Replace `useDarkMode` in `app-shell.tsx` with a small `useTheme` hook (put it in `src/hooks.ts`) storing `localStorage.theme` as `light`, `dark` or `system` (absent = `system`), resolving `system` via `matchMedia('(prefers-color-scheme: dark)')` and listening for changes while in `system`.
   - The `theme-toggle` button cycles **system -> light -> dark -> system**; its `aria-label`/`title` states the current mode (e.g. "Theme: system"), and its icon reflects the mode (lucide `Monitor`/`Sun`/`Moon`).
   - Update the pre-paint script in `index.html` to resolve the same logic before first paint, and remove the hard-coded `class="dark"` from `<html>`. Also set `color-scheme` on `<html>` so native controls and scrollbars match.
5. **Contrast table**: a comment block at the top of `index.css` listing each foreground/background pair, its ratio in light and dark, and the WCAG requirement it meets (1.4.3 or 1.4.11). Include pill text on its tinted background for all five tones (`ok`, `warn`, `bad`, `info`, `mute`) in both themes; adjust tint percentage or tone shade until each is >= 4.5:1.
6. **Update `HomePageSmokeTests`** so it creates its context with `ColorScheme = ColorScheme.Dark`; the existing "dark by default, toggle to light, toggle back" assertions must then pass with the new cycle order. Do not rename `theme-toggle`.

Visual drift on pages is expected and acceptable here; page-level restyling happens in later tasks.

## Files
- `src/AutoRemediator.Web/src/index.css` — tokens, contrast table, font, type floor, radius, shadow
- `src/AutoRemediator.Web/index.html` — pre-paint script, remove hard-coded `dark`
- `src/AutoRemediator.Web/src/hooks.ts` — `useTheme`
- `src/AutoRemediator.Web/src/components/app-shell.tsx` — use `useTheme`, three-state toggle (no other shell changes)
- `src/AutoRemediator.Web/src/main.tsx` — font import (if not done in CSS)
- `src/AutoRemediator.Web/package.json`, `package-lock.json` — `@fontsource-variable/inter`
- `tests/AutoRemediator.Web.FunctionalTests/HomePageSmokeTests.cs` — emulate dark color scheme

## Out of scope
- Skip link, nav semantics, drawer, focus management, global reduced-motion (task 02).
- Restyling individual components or using the new `--input`/shadow tokens in them (task 03).
- axe tests (task 04).

## Done when
- [ ] With no `localStorage.theme`, the app follows the OS scheme and switches live when the OS scheme changes.
- [ ] Reloading in each of the three modes shows no flash of the other theme (check with a throttled reload).
- [ ] `theme-toggle` cycles system -> light -> dark and its accessible name names the current mode.
- [ ] The contrast table in `index.css` lists every pair from step 1 and step 5 with ratios in both themes, and every listed ratio meets its requirement.
- [ ] No `text-[10.5px]`, `text-[11px]`, `text-[11.5px]` or other sub-12px sizes remain (`grep -rn "text-\[1[01]" src` finds nothing).
- [ ] Inter loads from the app's own origin (Network tab shows no third-party font request).
- [ ] `npm run lint && npm run typecheck && npm run build` pass.
- [ ] `HomePageSmokeTests` passes against a running app (`WEB_BASE_URL=<url> dotnet test tests/AutoRemediator.Web.FunctionalTests`).

## Notes
- Tailwind v4 maps tokens via `@theme inline`; add new `--color-*`, `--font-sans`, `--shadow-*` entries there so utilities like `shadow-card` and `font-sans` exist.
- `* { @apply border-border }` sets the default border colour; components that are controls must opt into `border-input` (task 03 does that).
- Use any contrast calculator (e.g. WebAIM) and record numbers to two decimals. Use measured ratios, not estimates.
- `color-mix(...)` pill backgrounds sit on `--card` or `--background`; measure the composited colour against both.
