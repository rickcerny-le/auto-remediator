## Why

The tool persists a `RemediationRun` per run (status, applied updates, PR link, errors) but there is no way to see them — the Web app is still the default Bootstrap template with no runs view. Slice 6 makes the work visible: a clean, GitHub-esque run history, and a cohesive modern UI to house it. This is the read-side payoff of everything built so far and needs no new external dependency (it reads the runs already stored).

## What Changes

- **Adopt MudBlazor as the UI design system** — add MudBlazor to the Blazor Web App and replace the default Bootstrap template with a GitHub-esque **dark** shell (app bar + navigation drawer), defaulting to dark with a **light/dark toggle**. Restyle the existing pages (Home, Configuration, Dependency Map) onto MudBlazor so the app is one consistent design.
- **Run-history API** — expose the persisted runs through the API: a global runs list (with an optional status filter) and a single-run detail, returning `Contracts` DTOs. Extend the run store with a global list and a by-id lookup.
- **Runs UI** — a global **Runs** page (an activity feed: repository, status, started/finished, update count, PR link, with a status filter) and a **Run detail** page (the updates table — package, from → to, kind (matched/collateral), beyond-policy flag — plus the PR link and any error). Loaded on navigation with a Refresh button.

Scope boundary: **read-only observability + restyle.** No new run behavior, no real-time push (manual refresh), no auth. Keep it simple.

## Capabilities

### New Capabilities
- `web-ui-foundation`: The shared MudBlazor design system — a GitHub-esque dark app shell (layout, app bar, navigation) with a dark-default theme and a light/dark toggle, applied across all pages.
- `run-observability-ui`: The run-history read surface — API endpoints listing runs (with status filter) and a single run's detail, and the Blazor Runs feed + Run detail pages.

### Modified Capabilities
<!-- None — the restyle satisfies the existing target-configuration/dependency-analysis UI requirements differently (cosmetic); run persistence is unchanged. -->

## Impact

- Adds the MudBlazor dependency to `AutoRemediator.Web` (+ `.Web.Client`); registers Mud services and providers; the layout/nav is rewritten and the existing pages are re-authored in MudBlazor components. No behavior change to those pages.
- Extends `IRemediationRunStore` with `ListAllAsync` (cross-partition) and `GetAsync(runId)`; adds run DTOs to `Contracts` and a `Runs` API feature slice (list + detail).
- No change to the workers, remediation logic, IaC, or CI. Still runs entirely locally against the emulators.
- Cost: none beyond the added client-side MudBlazor assets.
