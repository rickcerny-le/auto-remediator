## Context

Slice 6 — make the persisted runs visible and give the app a cohesive, modern look. Runs are already stored (`RemediationRun` in the `runs` table, partitioned by repository). The Web app is still the default Bootstrap Blazor Web App (Auto) template with Home/Configuration/Dependency-Map pages. This change adds MudBlazor, a GitHub-esque dark shell, and the runs read surface (API + pages).

Confirmed decisions: whole-app MudBlazor restyle; dark default + light toggle; a global runs feed + a run detail page with a status filter; manual refresh (no polling).

## Goals / Non-Goals

**Goals:**
- MudBlazor design system with a GitHub-esque dark shell (app bar + drawer), dark default + light toggle.
- Restyle Home, Configuration, Dependency Map onto MudBlazor (behavior unchanged).
- Runs API: global list (+ status filter) and single-run detail; store `ListAllAsync` + `GetAsync(runId)`.
- Runs feed page + Run detail page.

**Non-Goals:**
- New run behavior, real-time push (manual refresh only), auth, per-user prefs beyond the theme toggle.
- Removing the `.Web.Client` project (kept; see render-mode decision).

## Decisions

**1. Global `InteractiveServer` render mode for the Web app.**
Set `@rendermode="InteractiveServer"` on `<Routes/>` and `<HeadOutlet/>` in `App.razor`. Rationale: MudBlazor providers (theme/dialog/snackbar/popover) and the theme toggle need a live interactive context; a single global server render mode is the simplest, most reliable MudBlazor-on-Blazor-Web-App setup and removes per-page `@rendermode` juggling. *Trade-off:* supersedes the Slice-1 Auto (server+WASM) default — the `.Web.Client` project stays referenced but is not the active render path. Acceptable for a local tool; Auto can be revisited later. *Alternative:* keep Auto with per-component modes — fiddly provider/layout boundaries; rejected for simplicity.

**2. MudBlazor setup.**
`AddMudServices()` in `Program`; MudBlazor CSS/JS + fonts via `_content/MudBlazor/...` static assets in `App.razor` (drop the template's Bootstrap link); `MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider` in `MainLayout`. Pin a MudBlazor version compatible with .NET 10 (resolve at apply). Rationale: standard MudBlazor wiring.

**3. Shell + theme.**
`MainLayout` = `MudLayout` (`MudAppBar` with title + theme-toggle `MudIconButton`, `MudDrawer` with `MudNavMenu`/`MudNavLink` to Home, Configuration, Dependency Map, Runs, `MudMainContent`). A `MudTheme` defines a GitHub-esque dark palette (dark greys, subtle borders, blue/green accents) and a light palette; `MudThemeProvider @bind-IsDarkMode` holds the mode, default dark, flipped by the toggle. Theme choice may be persisted to `localStorage` (optional; default-dark otherwise). Rationale: matches the requested look with minimal custom CSS.

**4. Restyle existing pages (behavior-preserving).**
Configuration → `MudTextField`/`MudSwitch`/`MudSelect`/`MudTable`/`MudButton`; Dependency Map → `MudTable` with status `MudChip`s; Home → a `MudPaper` hero. Same API calls and logic; only markup changes. Rationale: one visual language without touching behavior (existing target-configuration/dependency-analysis requirements still hold).

**5. Runs read model + store additions.**
`IRemediationRunStore` gains `ListAllAsync()` (cross-partition `QueryAsync` with no partition filter) and `GetAsync(Guid runId)` (query `RowKey == runId`, cross-partition, first match). Rationale: the global feed needs all partitions and detail is by id; cross-partition scans are fine at this scale. `Contracts` gets `RunSummaryDto` (id, repositorySlug, status, startedAt, finishedAt, updateCount, pullRequestUrl) and `RunDetailDto` (+ updates: `[{packageId, from, to, kind, beyondPolicy}]`, error). Sorting newest-first by startedAt.

**6. Runs API as a vertical slice.**
`Features/Runs`: `GET /api/runs?status=` (list, optional status filter, newest first) and `GET /api/runs/{id}` (detail, 404 when absent), returning the DTOs. Endpoints resolve `IRemediationRunStore` directly (existing convention). Rationale: consistent with the other feature slices.

**7. Runs pages.**
`/runs` — `MudTable` of `RunSummaryDto` with a `MudSelect` status filter and a Refresh button; status rendered as a colored `MudChip` (Completed→success, Failed→error, NoUpdates→default, in-progress→info); PR link as an external `MudLink`; row click → `/runs/{id}`. `/runs/{id}` — run header (status/timestamps/PR/error) + a `MudTable` of updates (package, from → to, Kind chip, "beyond policy" badge). Loaded on navigation with Refresh; `InteractiveServer`.

## Risks / Trade-offs

- **[MudBlazor × .NET 10 compatibility]** → Pin a MudBlazor version that targets net10; if the newest lags, use the latest compatible and note it. Resolve at apply.
- **[Render-mode switch to global server]** → Diverges from the Auto setup and idles the WASM client. Mitigation: documented; simplest reliable MudBlazor path; reversible.
- **[Cross-partition run queries]** → `ListAllAsync`/by-id scans grow with run volume. Mitigation: fine for the test scale; add pagination/secondary index later if needed.
- **[Restyle regressions]** → Re-authoring pages could break existing behavior. Mitigation: keep the same handlers/API calls; the API integration tests still cover the endpoints; smoke via the Playwright test on the new shell.

## Migration Plan

Additive UI + read API. Store gains two query methods; DTOs/feature slice added. The Web app's render mode changes to global InteractiveServer and its markup is re-authored — no server/worker/IaC/CI impact. Rollback = revert. Runs with no history simply show an empty feed.

## Open Questions

- Exact MudBlazor version for .NET 10 (resolve at apply).
- Whether to persist the theme choice in `localStorage` (nice-to-have; default dark otherwise).
- Pagination for the runs feed if history grows (out of scope now; newest-first full list).
