# 04 AppHost: provisioning at startup and the reset resource

**Status:** todo
**Feature:** 003-self-provisioning-scenarios
**Depends on:** 03

## What to do
Wire the wrapper into the AppHost so that, in local mode, `.local` is built or confirmed before anything that reads it starts.

1. **Confirm the Aspire semantics before wiring, then note what you saw in the PR description.** Use Aspire 13.5.1 and a throwaway local edit; do not commit it. Check three things:
   - a project resource that exits 0 lets `WaitForCompletion` dependents start;
   - one that exits non-zero leaves them `FailedToStart` (or the observed state);
   - restarting a `Finished` resource from the dashboard re-runs it.

   If any of these differs, stop and report it to Benny before going on.
2. In `AutoRemediator.AppHost.csproj`, add a `ProjectReference` to `tools/local-scenarios/LocalScenarioSetup/LocalScenarioSetup.csproj` (like `HeldRunSeeder`).
3. In `AppHost.cs`, inside local mode only:
   - Read `LocalRepos:ResetOnStart`. A missing or unparseable value means `true`, the same parsing style as `Model:Enabled`.
   - Compute `scriptsDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "tools", "local-scenarios"))`.
   - Register `local-scenarios`:

     ```
     builder.AddProject<Projects.LocalScenarioSetup>("local-scenarios")
         .WithArgs("provision", "--scripts", scriptsDir, "--root", localRoot [, "--no-reset"])
     ```

     It has no `WaitFor`, so it runs in parallel with the emulators.
   - Register `local-scenarios-reset`: the same project with `reset` arguments, `.WaitForCompletion(localScenarios)` and `.WithExplicitStart()`.
   - Add `.WaitForCompletion(localScenarios)` to `api`, `scheduler`, `remediation` and `held-run-seeder`. The `WithLocalRepos` helper is the natural place, provided `localScenarios` is declared before it is used. Otherwise add the call at each site. Keep the existing `WaitFor(storage)`/`WaitFor(serviceBus)`.
   - Extend the startup `Console.WriteLine` to include `reset on start <true|false>`.
   - Update the `held-run-seeder` comment. It no longer needs the generator run by hand, but still needs enrollment, which task 05 makes automatic.
4. With `LocalRepos:Enabled` unset or false, nothing changes: no new resources, no new waits.

## Files
- `src/AutoRemediator.AppHost/AppHost.cs`: the two resources and the waits
- `src/AutoRemediator.AppHost/AutoRemediator.AppHost.csproj`: the project reference

## Out of scope
- Enrollment and the `scheduler` → enroll wait (task 05).
- The `api` health check (task 05).
- Docs (task 06).

## Done when
- [ ] `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass (run `aspire stop` first).
- [ ] With `.local` deleted, `$env:LocalRepos__Enabled='true'; $env:Model__Enabled='false'; aspire start`:
  - `aspire describe` shows `api`, `scheduler` and `remediation` waiting until `local-scenarios` is `Finished` (exit 0);
  - `aspire logs local-scenarios` shows the rebuild;
  - `.local/scenarios.fingerprint.json` exists afterwards.
- [ ] `aspire stop`, then `aspire start` again: the `local-scenarios` logs say "up to date" plus the reset lines, and it finishes in under 15 s.
- [ ] With `$env:LocalRepos__ResetOnStart='false'`, the warm start skips without resetting.
- [ ] `aspire resource local-scenarios-reset start` runs the fast reset; its logs show it.
- [ ] With PATH lacking `pwsh` for the AppHost (start `aspire` from a shell whose PATH has the pwsh folder removed), `local-scenarios` exits 2, its console log shows the pwsh hint, and `api`/`scheduler`/`remediation` do not start. Record the state they end in.
- [ ] Without `LocalRepos__Enabled`, `aspire describe` lists the same resources as before this feature.

## Notes
- `WaitForCompletion` takes an optional expected exit code (default 0); use the default.
- `aspire start` stops a previous instance. If that instance was mid-provisioning, the deleted-first fingerprint (task 01) guarantees a rebuild.
- Remember the user's rule: use `aspire start`/`aspire stop` from PowerShell, never `dotnet run` on the AppHost.
