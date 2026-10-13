# frontend memory

## Learnings
- 2026-10-13 — No Python on this machine; for multi-line scripted edits, write a `.cjs` file with the Write tool and run it with node. Bash heredocs break on backticks and quotes.
- 2026-10-13 — Repo files mix CRLF and LF line endings; run `file <path>` before editing and preserve each file's endings. `sed -i 's/\r$//'` normalizes a file to LF.
- 2026-10-13 — The web SPA lives in `src/AutoRemediator.Web` (Vite, React 19, React Router 8); routes are defined with `createBrowserRouter` in `src/main.tsx`.
- 2026-10-13 — Tailwind v4 tokens are in `src/index.css` under `@theme inline`; the contrast table in that file's header records measured ratios, so update it when changing any colour pair.
- 2026-10-13 — Local shadcn primitives in `src/components/ui/` are built on the `radix-ui` umbrella package; edit them directly and keep `cva` variant names unchanged so call sites do not break.
- 2026-10-13 — Use the shared `focusRing` class from `src/lib/utils.ts` for any new interactive primitive, so focus styling stays consistent (2px, 2px offset, at least 3:1).
- 2026-10-13 — `DataTable` requires a `caption` prop, takes `rowHref` and `primaryColumnId`, and the primary cell's link is the only tab stop per row. There is no `onRowClick`.
- 2026-10-13 — Each page renders its own `PageHeader`, so route-change effects (such as focusing the `h1`) need module-level state, not a ref, because the component instance changes per page.
- 2026-10-13 — Inter comes from `@fontsource-variable/inter`; its family name is "Inter Variable".
- 2026-10-13 — Frontend checks to run in `src/AutoRemediator.Web`: `npm run lint && npm run typecheck && npm run build`.
