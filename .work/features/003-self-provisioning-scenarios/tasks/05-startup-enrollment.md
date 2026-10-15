# 05 Enrollment at startup and scheduler ordering

**Status:** done
**Feature:** 003-self-provisioning-scenarios
**Depends on:** 04

## What to do
Make enrollment automatic on every local-mode start (Azurite is not persisted, so it is always empty), and make the scheduler's startup pass run after it.

1. **Wrapper `enroll` mode** (`LocalScenarioSetup`):
   - Usage: `enroll --scripts <dir> --root <dir> --profile <name>`. Preflight needs `pwsh` only.
   - Validate `--profile` against `baseline|patch|major|prerelease` before anything else. An invalid value exits 1 with `LocalRepos__Profile '<value>' is not valid; use baseline, patch, major or prerelease.`
   - Resolve the API base URL from configuration: environment variables `services__api__http__0`, else `services__api__https__0`, injected by `WithReference(api)`. Read them with `Environment.GetEnvironmentVariable` or a minimal `ConfigurationBuilder().AddEnvironmentVariables()`. If neither is set, exit 1 with "No API URL was injected; enroll runs as the Aspire resource local-scenarios-enroll." Never fall back to a hard-coded port.
   - Poll `GET <url>/health` every 2 s for up to 120 s, as a guard on top of `WaitFor`. On timeout, exit 1 with the last error.
   - Run `pwsh -NoProfile -NonInteractive -File <scripts>/Set-LocalScenarioConfig.ps1 -ApiUrl <url> -Profile <p> -Root <root>`, streaming its output, and return its exit code.
   - Put the URL selection and profile validation in testable helpers, and add tests to `tests/AutoRemediator.Tools.Tests`: http preferred over https, https fallback, none set → error, invalid profile → error, and the argument list for enroll.
2. **`Set-LocalScenarioConfig.ps1`:** add `$ProgressPreference = 'SilentlyContinue'`, and set plain output rendering when output is redirected (as task 01 does). Keep the parameters and behaviour unchanged. Make the "Next step: restart the scheduler" line say instead that, when run by the AppHost at startup, the scheduler pass follows automatically. A simple approach: print it only when the environment variable `LOCAL_SCENARIOS_STARTUP` is not set, and have the wrapper set it for the child.
3. **AppHost (`AppHost.cs`, local mode only):**
   - Read `LocalRepos:Profile` (default `baseline`). Pass it through as given; the wrapper validates it, so a bad value fails the resource, not the AppHost.
   - Add `.WithHttpHealthCheck("/health")` to `api`, in local mode only. Do it through `WithLocalRepos` or an `if`, so the Azure DevOps composition is unchanged.
   - Register `local-scenarios-enroll`:

     ```
     builder.AddProject<Projects.LocalScenarioSetup>("local-scenarios-enroll")
         .WithArgs("enroll", "--scripts", scriptsDir, "--root", localRoot, "--profile", profile)
         .WithReference(api)
         .WaitFor(api)
     ```

   - Add `.WaitForCompletion(enroll)` to `scheduler` only when `LocalRepos:RunOnStart` is true (task 04 already registers the scheduler with `WithExplicitStart()` otherwise, and the user decided it is opt-in, so enrollment must not gate it by default). Leave `remediation`, `web` and `held-run-seeder` alone, so a failed enrollment does not stop the app.
   - Include `profile <name>` in the startup `Console.WriteLine`.

## Files
- `tools/local-scenarios/LocalScenarioSetup/*`: the `enroll` mode
- `tests/AutoRemediator.Tools.Tests/*`: enroll tests
- `tools/local-scenarios/Set-LocalScenarioConfig.ps1`: quiet output and the next-step line
- `src/AutoRemediator.AppHost/AppHost.cs`: health check, enroll resource, scheduler wait, profile

## Out of scope
- An `-Only` subset at startup.
- Switching profile without a restart. That stays the manual `Set-LocalScenarioConfig.ps1 -Profile <p>` plus a scheduler restart; task 06 documents it.
- Any change to `held-run-seeder`.

## Done when
- [ ] `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass, and the enroll tests run.
- [ ] A local-mode `aspire start` with no `LocalRepos__Profile` shows the following:
  - `local-scenarios-enroll` `Finished` (exit 0), and its log shows the created rows and "Active profile: baseline";
  - `GET /api/repositories` lists the 15 enrolled scenario repos (14 enabled plus `disabled-repo`);
  - `GET /api/settings` shows strategy `Minor` and the absolute `.local/feed` path;
  - with `LocalRepos__RunOnStart=true`, `scheduler` starts after enrollment, and its pass creates runs for the enabled repos; by default it stays `NotStarted` until started by hand.
- [ ] `$env:LocalRepos__Profile='major'` gives strategy `Major`.
- [ ] `$env:LocalRepos__Profile='bogus'`:
  - `local-scenarios-enroll` exits 1 with the valid-values message;
  - `api`, `web` and `remediation` are `Running`;
  - `scheduler` does not start (record its state);
  - after fixing the value through a restart, everything works.
- [ ] The enroll resource never uses a hard-coded port: grep `AppHost.cs` and the wrapper for `localhost:`.
- [ ] Without `LocalRepos__Enabled`, `api` has no health check annotation and there is no enroll resource.

## Notes
- `Set-LocalScenarioConfig.ps1` already upserts by name and is idempotent. Restarting `local-scenarios-enroll` by hand must be safe.
- `/health` is mapped only in Development (`ServiceDefaults.MapDefaultEndpoints`). Aspire runs projects as Development locally. If a non-Development launch profile is ever used, the health check would fail, so note it in the code comment.
- The env var used to suppress the next-step line is an internal detail between the wrapper and the script; mention it in the script's help.
