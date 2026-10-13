# Decisions

Append-only.

## 2026-10-13 · 001-ui-redesign
- **Theme defaults to `system`** (modes light | dark | system, applied pre-paint, no flash). Reason: respects OS preference; user chose it.
- **Inter self-hosted** via `@fontsource-variable/inter`, no CDN. Reason: user approved a web font; self-hosting avoids an external dependency.
- **axe runs in the C# functional tests via `Deque.AxeCore.Playwright`**, not `@axe-core/playwright`. Reason: the test project is C#; same engine and rule tags. User approved.
- **DataTable rows navigate through a real link in the primary cell**; row click is a mouse convenience only. `caption` is a required prop so every table has an accessible name.
- **Desktop sidebar and mobile drawer are separate renders** (matchMedia hook), keeping one nav landmark and a unique `data-testid="nav"`.
- **Light `--success` split from `--primary`** (#166c2e text/pills vs #1a7f37 button) to meet 4.5:1.
- **Dark outline buttons are transparent** (was `bg-input/30`) after axe found a Discard contrast failure; affects every outline button in dark mode.
- **Table scroll regions are always focusable** (`role="region"`, `tabIndex=0`); conditional focus would need overflow detection.
- **Scope kept small**; charts, logo, density toggle, i18n, extra animation deferred.
