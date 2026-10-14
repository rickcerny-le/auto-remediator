# 05 AppHost: local-repos mode

**Status:** done
**Feature:** 002-local-repo-mode
**Depends on:** 01, 04

## What to do
Add a local-repos switch to `src/AutoRemediator.AppHost/AppHost.cs`, next to the `Model:Enabled` switch that task 04 adds. With the switch unset, the composed application must be exactly what task 04 left.

1. **`LocalRepos:Enabled`** (default `false`; parse it the same way as `Model:Enabled`, but treat a missing or unparseable value as `false`) and **`LocalRepos:Root`**. The root defaults to `Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".local"))`, the repository's `.local` folder. When enabled, add these environment variables to `api`, `scheduler` and `remediation`:
   - `AzureDevOps__Mode=Local`
   - `AzureDevOps__LocalRoot=<root>\repos`
   - `AzureDevOps__LocalPullRequestsRoot=<root>\prs`

   No PAT, organization URL, Key Vault or extra resource is needed. Use one small local function, such as `WithLocalRepos(project)`, so the three projects stay consistent.
2. Log (or print at startup) one line stating which repository mode is active and the root, and whether the model is enabled, so `aspire logs` shows it.
3. README: add `LocalRepos:Enabled` and `LocalRepos:Root` to the Configuration table (task 04 adds `Model:Enabled`). Add a short "Local repository mode" subsection under "Run locally (Aspire)" that shows the PowerShell form below and points to `docs/local-testing.md` (written in task 10):
   ```powershell
   $env:Model__Enabled='false'; $env:LocalRepos__Enabled='true'; aspire start
   ```
   Explain in one sentence that with `Model__Enabled=false`, remediation is non-agentic: a build that fails after the bump ends `VerificationFailed` with no PR.

## Files
- `src/AutoRemediator.AppHost/AppHost.cs`: local-repos switch
- `README.md`: configuration rows and run instructions

## Out of scope
- The model provider and `Model:Enabled` (task 04).
- Seeding data or triggering runs from the AppHost.
- Pinning Azurite ports.

## Done when
- [ ] `git diff` shows the default path unchanged in substance: with `LocalRepos:Enabled` unset, the same resources and references are created in the same order as after task 04, and no `AzureDevOps__*` variable is added.
- [ ] `$env:LocalRepos__Enabled='true'; $env:Model__Enabled='false'; aspire start`:
  - the `remediation`, `api` and `scheduler` resources show `AzureDevOps__Mode=Local` and the absolute `LocalRoot`/`LocalPullRequestsRoot` in their environment (dashboard resource details)
  - `web` does not get them
  - the API's `GET /api/dependency-map` returns 200 with no repositories enrolled
- [ ] `$env:LocalRepos__Enabled='true'; aspire start` (model enabled) also starts, with `ollama`/`chat` present. The two switches are independent.
- [ ] `dotnet build AutoRemediator.sln` succeeds, and `aspire stop` leaves no locked files.

## Notes
- Use the Aspire CLI from PowerShell (`aspire start --non-interactive`, `aspire describe`, `aspire stop`); never `dotnet run` the AppHost.
- Env vars set in the shell before `aspire start` reach the AppHost's configuration. User secrets on the AppHost (`LocalRepos:Enabled`) are an alternative.
- Reference shape: `WithGit` in `git show feat/local-remediation:src/AutoRemediator.AppHost/AppHost.cs`. Adapt it to the `AzureDevOps__*` keys from task 01; the `Git:*` keys are not used.
- `api` needs local mode too: the dependency map reads manifests through `IAzureDevOpsClient`, and task 09 serves the PR diff from the local repos.
