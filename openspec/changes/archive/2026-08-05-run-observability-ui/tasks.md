## 1. MudBlazor setup

- [x] 1.1 Add the MudBlazor package to `AutoRemediator.Web` (+ `.Web.Client` if components live there); pin a version compatible with .NET 10
- [x] 1.2 `AddMudServices()` in `Program`; add MudBlazor CSS/JS + fonts via `_content/MudBlazor/...` in `App.razor` and drop the Bootstrap link
- [x] 1.3 Set global `@rendermode="InteractiveServer"` on `<Routes/>` and `<HeadOutlet/>` in `App.razor`

## 2. Shell + theme

- [x] 2.1 Define a `MudTheme` with a GitHub-esque dark palette and a light palette
- [x] 2.2 Rewrite `MainLayout` as `MudLayout` (`MudAppBar` with title + theme-toggle button, `MudDrawer` with `MudNavMenu` links to Home/Configuration/Dependency Map/Runs, `MudMainContent`); `MudThemeProvider @bind-IsDarkMode` (default dark) + popover/dialog/snackbar providers
- [x] 2.3 Wire the app-bar toggle to flip dark/light (optional: persist to `localStorage`)

## 3. Restyle existing pages (behavior-preserving)

- [x] 3.1 Home → a MudBlazor hero (`MudPaper`/`MudText`)
- [x] 3.2 Configuration → MudBlazor form (`MudTextField`/`MudSwitch`/`MudSelect`) + `MudTable` for repos; same API calls/logic
- [x] 3.3 Dependency Map → `MudTable` with status `MudChip`s; same load/refresh logic
- [x] 3.4 Remove the template's Counter/Weather sample pages if still present

## 4. Run store + DTOs

- [x] 4.1 Extend `IRemediationRunStore` with `ListAllAsync()` (cross-partition) and `GetAsync(Guid runId)` (by RowKey); implement in the Table store
- [x] 4.2 Add `RunSummaryDto` and `RunDetailDto` (with an updates list carrying package, from, to, kind, beyondPolicy) to `Contracts`

## 5. Runs API

- [x] 5.1 Add a `Features/Runs` slice: `GET /api/runs?status=` (list, optional status filter, newest first) mapping runs → `RunSummaryDto`
- [x] 5.2 `GET /api/runs/{id}` → `RunDetailDto` (404 when absent)

## 6. Runs UI

- [x] 6.1 `/runs` page: `MudTable` of runs (repository, status chip, started, update count, PR link), `MudSelect` status filter, Refresh button, row → `/runs/{id}`
- [x] 6.2 `/runs/{id}` page: run header (status/timestamps/PR/error) + `MudTable` of updates (package, from → to, kind chip, beyond-policy badge)
- [x] 6.3 Add the Runs link to the nav drawer

## 7. Tests

- [x] 7.1 API integration tests for `/api/runs` (list + status filter) and `/api/runs/{id}` (found + 404), services overridden via `WebApplicationFactory`
- [x] 7.2 Run-store test for `ListAllAsync`/`GetAsync(runId)` against Azurite (skips when the emulator is not running)
- [x] 7.3 Update the Playwright smoke test for the new shell (assert the app bar/nav and the Runs page load)

## 8. Verification

- [x] 8.1 `dotnet build AutoRemediator.sln` — zero errors
- [x] 8.2 `dotnet test AutoRemediator.sln` — all tests pass (emulator/live tests self-skip)
- [x] 8.3 (Manual/live) Run the AppHost; confirm the dark shell, theme toggle, restyled pages, and the Runs feed + detail render
