# tester memory

## Learnings
- 2026-10-13 — Functional tests are C# xUnit v3 with Microsoft.Playwright in `tests/AutoRemediator.Web.FunctionalTests`; they are skipped unless `WEB_BASE_URL` is set.
- 2026-10-13 — axe runs through the `Deque.AxeCore.Playwright` NuGet package, not `@axe-core/playwright`: use `using Deque.AxeCore.Commons;` and `page.RunAxe(new AxeRunOptions{RunOnly=new RunOnlyOptions{Type="tag",Values=[...]}})`. There is no `AxeBuilder`.
- 2026-10-13 — Before running axe, `await document.getAnimations()` until animations finish; fixed sleeps are flaky.
- 2026-10-13 — Start the app with `aspire start` and stop it with `aspire stop` from PowerShell; never `dotnet run` the AppHost.
- 2026-10-13 — The Web URL is the Vite dev server and is not shown in `aspire describe`; probe ports to find it.
- 2026-10-13 — `ReviewGateTests` needs `ConnectionStrings__tables` and `ConnectionStrings__blobs` set to the Aspire Azurite endpoints.
