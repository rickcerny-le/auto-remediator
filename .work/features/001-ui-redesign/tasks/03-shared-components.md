# 03 Shared components and keyboard-operable tables

**Status:** done
**Feature:** ui-redesign
**Depends on:** 01

## What to do
Apply the new tokens to the shared primitives and fix table operability.

1. **Focus ring** for every interactive primitive: `focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring` (or an equivalent ring), replacing the current `ring-[3px] ring-ring/50` (50% alpha is not guaranteed 3:1). Put the shared class string in one place (e.g. a constant in `src/lib/utils.ts`) if it is repeated.
2. **Restyle** `button.tsx`, `input.tsx`, `select.tsx`, `switch.tsx`, `accordion.tsx`, `card.tsx`, `alert.tsx`, `label.tsx`, `table.tsx`:
   - Controls (input, select trigger, outline button, switch unchecked track) use `border-input` so their boundary is >= 3:1.
   - Cards: 12px radius, `shadow-card` in light, one-step-lighter surface in dark, generous padding, `border-border` (decorative).
   - Disabled states stay distinguishable without relying on reduced opacity alone for text that must be read.
   - Minimum target size 24x24 CSS px for all controls (WCAG 2.5.8); `icon-xs` buttons must meet it.
3. **Pills** (`pill.tsx`, `.ar-pill` in `index.css`): confirm each tone's text-on-tint meets 4.5:1 in both themes (using the numbers recorded in the task 01 contrast table). The text label is always present; the dot is `aria-hidden` and never the only signal.
4. **DataTable** (`data-table.tsx`):
   - Replace `onRowClick` with a row-link API, e.g. `rowHref?: (row: T) => string` plus a `primaryColumnId` (default: first column). The primary cell renders a React Router `<Link>` to that href. Keep whole-row click as a mouse convenience that delegates to the link (no extra tab stops; ignore clicks that started on another link or a text selection).
   - Add an accessible name: a `caption` prop rendered as `<caption>` (visually hidden allowed) or an `aria-label`.
   - Header cells render `scope="col"` (`TableHead` in `ui/table.tsx`).
   - Row hover and the focus-within state on the row are visible.
   - Update the three call sites (`pages/home.tsx` held and recent tables, `pages/runs.tsx`) to the new API, with captions such as "Runs awaiting review", "Recent runs", "Runs".
5. **run-cells.tsx**: the "—" placeholder in `PullRequestLink` uses the fixed `--subtle` and gets an accessible name ("No pull request", e.g. `aria-label` on the span or visually hidden text). Remove `e.stopPropagation()` only if the new row-click logic makes it redundant.
6. **ar-label / ar-tag** classes keep the 12px floor from task 01 and use `--muted-foreground`, not `--subtle`.

## Files
- `src/AutoRemediator.Web/src/components/ui/button.tsx`, `input.tsx`, `select.tsx`, `switch.tsx`, `accordion.tsx`, `card.tsx`, `alert.tsx`, `label.tsx`, `table.tsx` — restyle, focus ring, `scope="col"`
- `src/AutoRemediator.Web/src/components/data-table.tsx` — row link API, caption, row states
- `src/AutoRemediator.Web/src/components/pill.tsx`, `run-cells.tsx` — `aria-hidden` dot, placeholder name
- `src/AutoRemediator.Web/src/index.css` — pill/label/tag adjustments
- `src/AutoRemediator.Web/src/lib/utils.ts` — shared focus-ring class (optional)
- `src/AutoRemediator.Web/src/pages/home.tsx`, `pages/runs.tsx` — call-site updates only (no layout changes)

## Out of scope
- Page layout, KPI sizing, spacing on individual pages (task 04).
- Shell and nav (task 02).
- New components (no tooltips, no menus).

## Done when
- [ ] Tabbing through Runs reaches one link per row (in the Repository cell); Enter opens the run detail page. Same for both Home tables.
- [ ] Mouse click anywhere on a row still opens the run; clicking the PR "open" link opens the PR only.
- [ ] Every table has an accessible name and every `<th>` has `scope="col"` (inspect DOM).
- [ ] Every button, input, select, switch and accordion trigger shows a 2px focus outline with 2px offset in both themes.
- [ ] Input, select and switch boundaries measure >= 3:1 against their background in both themes.
- [ ] No `onRowClick` prop remains (`grep -rn onRowClick src` is empty).
- [ ] All existing `data-testid`s on these components and their call sites are still present (`runs-table`, `held-table`, `recent-runs`, `status-filter`, `allow-prerelease`, etc.).
- [ ] `npm run lint && npm run typecheck && npm run build` pass; `ReviewGateTests` passes (it checks `runs-table` and `held-table` content).

## Notes
- Task 03 depends only on 01, so it can run in parallel with 02 if needed, but both touch `index.css`; do them in order to avoid conflicts.
- shadcn primitives here are local copies; edit them directly. Keep `cva` variant names unchanged so call sites do not break.
- The row link should be the visible repository text itself, not an extra "View" link, so the accessible name is meaningful (e.g. "contoso/api").
