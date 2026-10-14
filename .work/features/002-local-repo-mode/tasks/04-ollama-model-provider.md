# 04 Ollama as the model provider, with an optional model

**Status:** done
**Feature:** 002-local-repo-mode
**Depends on:** none (independent of 01–03)

## What to do
Replace Azure AI Foundry with Ollama as the model provider, as the user decided (see decisions.md, 2026-10-13 · 002). Add the `Model:Enabled` switch so the model resource can be left out entirely. Copy the changes from commit `5071d9f` (and the `Model:Enabled` and soft-dependency parts of `c336726`) on the unmerged branch `feat/local-remediation`. Inspect them with `git show 5071d9f`, `git show c336726` and `git diff main feat/local-remediation -- <path>`. **Do not merge, cherry-pick or rebase that branch.** Re-apply the changes by hand onto the current code, which has moved on (React UI, `Aspire.Hosting.JavaScript`).

1. **AppHost** (`src/AutoRemediator.AppHost/AppHost.cs`, `.csproj`):
   - Remove `using Aspire.Hosting.Foundry`, the `AddFoundry("foundry").RunAsFoundryLocal()` / `AddDeployment("chat", ...)` block, and the `Aspire.Hosting.Foundry` package reference.
   - Add `CommunityToolkit.Aspire.Hosting.Ollama` (13.5.0, as on the branch) and `using CommunityToolkit.Aspire.Hosting.Ollama;`.
   - `Model:Enabled` (default `true`): parse with `bool.TryParse`, and treat a missing or unparseable value as `true`, exactly as the branch does (`var modelEnabled = !bool.TryParse(builder.Configuration["Model:Enabled"], out var enabled) || enabled;`). When it is `true`, create `builder.AddOllama("ollama").WithDataVolume().AddModel("chat", "phi4")`. When it is `false`, create no `ollama` or `chat` resource at all.
   - `remediation` gets `.WithReference(chat)` only when the model is enabled. Keep the "deliberately no `WaitFor(chat)`" comment, reworded for Ollama (an Ollama container still pulling the model must not block dependency updates).
   - Keep every other existing comment in `AppHost.cs`. The branch commit deleted unrelated comments (Azurite flag, scheduler storage note, section headers); do not copy those deletions.
2. **Agents** (`src/AutoRemediator.Agents`):
   - `.csproj`: replace `Aspire.Azure.AI.Inference` with `CommunityToolkit.Aspire.OllamaSharp` (13.5.0).
   - `AgentsExtensions.AddAgents`: `builder.AddOllamaApiClient(AgentsOptions.ChatConnectionName).AddChatClient();`, still guarded by `HasChatConnection`. With no `ConnectionStrings:chat`, nothing is registered and `MafRemediationAgent.IsAvailable` stays `false`.
   - Doc comments in `AgentsExtensions.cs`, `AgentsOptions.cs` and `MafRemediationAgent.cs`: replace the Foundry wording as the branch does.
   - Soft dependency (from `c336726`): in `MafRemediationAgent.ProposeAsync`, catch any non-cancellation exception from the chat call and return `RemediationProposal.None($"the model could not be reached ({ex.GetType().Name}: {ex.Message})")` with a warning log. An Ollama resource that is up but has not finished pulling `phi4` answers 404, and that must cost the repair, not the run.
3. **Tests:**
   - `tests/AutoRemediator.Agents.Tests/AgentsExtensionsTests.cs`: the "chat client is registered" test uses `Endpoint=http://localhost:11434;Model=phi4`.
   - `tests/AutoRemediator.Agents.Tests/MafRemediationAgentTests.cs`: add `A_model_that_cannot_be_reached_ends_the_attempt_without_failing_the_run` and the `Failure` property on the fake chat client, as in `c336726`.
   - `LocalModelAgentTests.cs` and `tests/AutoRemediator.Infrastructure.Tests/RemediationLoopLocalModelTests.cs`: change the doc comments to Ollama, and state the `AUTOREMEDIATOR_CHAT_CONNECTION` format: `Endpoint=http://localhost:11434;Model=<name>`.
4. **Aspire package versions.** The branch also bumped all Aspire packages and the AppHost SDK from 13.5.1 to 13.5.3. First try the two CommunityToolkit packages against the current 13.5.1 versions. Only if restore reports a downgrade (NU1605) or a missing API, bump **every** Aspire package and `Aspire.AppHost.Sdk` together to one version (13.5.3 is in the local cache), including `Aspire.Hosting.JavaScript`, which did not exist on the branch. Record which path you took in the review notes.
5. **README:** replace the Foundry wording.
   - Prerequisites: replace the Foundry Local bullet with Ollama. It runs as a container started by the AppHost, so no separate install is needed beyond the container runtime. The first start pulls `phi4` (several GB) into the `ollama` data volume. Set `Model__Enabled=false` to skip it.
   - "AI remediation loop" table: the local column becomes "Ollama (`phi4`), started by the AppHost unless `Model:Enabled=false`". In the deployed column, replace the Foundry text with "To be decided: the Terraform still provisions Azure AI Foundry, which the Ollama client cannot call", and link to the FEATURE.md risk. Do not change `infra/`.
   - The "Without Foundry Local installed..." sentence becomes "With the model disabled or unreachable...".
   - Configuration table: update the `ConnectionStrings:chat` row (Ollama, format above), and add a `Model:Enabled` row.
   - Line 4 and the project-structure line that mention Azure AI Foundry: say Ollama.
6. **PRD:** no change. `docs/PRD.md` does not mention Foundry; it only says "local model resource". Confirm with grep.

## Files
- `src/AutoRemediator.AppHost/AppHost.cs`: Ollama and `Model:Enabled`
- `src/AutoRemediator.AppHost/AutoRemediator.AppHost.csproj`: package swap (and the version bump only if step 4 needs it)
- `src/AutoRemediator.Agents/AutoRemediator.Agents.csproj`: package swap
- `src/AutoRemediator.Agents/AgentsExtensions.cs`, `AgentsOptions.cs`, `MafRemediationAgent.cs`
- `tests/AutoRemediator.Agents.Tests/AgentsExtensionsTests.cs`, `MafRemediationAgentTests.cs`, `LocalModelAgentTests.cs`
- `tests/AutoRemediator.Infrastructure.Tests/RemediationLoopLocalModelTests.cs`: doc comment only
- `src/AutoRemediator.Infrastructure/AutoRemediator.Infrastructure.csproj` (and other projects): only if step 4 needs the version bump
- `README.md`: model wording, prerequisites, configuration rows

## Out of scope
- Local repository mode and the `LocalRepos:*` switch (task 05).
- `infra/terraform` (still provisions Foundry) and deployed model wiring. That is an open question in FEATURE.md.
- Pinning the Ollama host port, choosing a different model, or GPU configuration.
- Anything else from `feat/local-remediation` (`LocalGitClient`, `MVP.md`, the fixture script).

## Done when
- [ ] `git grep -i foundry -- src tests README.md` returns nothing. `infra/` and `.claude/` are not touched.
- [ ] `docs/PRD.md` is unchanged by this task.
- [ ] `dotnet build AutoRemediator.sln` succeeds with no new warnings, and no NU1605/NU1608 warnings for Aspire or CommunityToolkit packages.
- [ ] `dotnet test tests/AutoRemediator.Agents.Tests` passes, including the new unreachable-model test and the registration test with the Ollama connection string. `dotnet test AutoRemediator.sln` passes.
- [ ] `aspire start` with no env vars: `aspire describe` lists `ollama` and `chat`, `ollama` reaches Running, and `remediation`'s environment has a `ConnectionStrings__chat` of the form `Endpoint=http://...;Model=phi4`.
- [ ] `$env:Model__Enabled='false'; aspire start`: `aspire describe` lists no `ollama`/`chat` resource; `api`, `web`, `scheduler` and `remediation` reach Running; `remediation` has no `ConnectionStrings__chat`.
- [ ] With the model enabled and `phi4` pulled, `AUTOREMEDIATOR_CHAT_CONNECTION` set to the `chat` connection string from `aspire describe` makes `LocalModelAgentTests` run (not skip) and pass. Record the result in the review notes; a failure caused only by model quality is acceptable, because the test asserts mechanics.
- [ ] `aspire stop` leaves no locked files.

## Notes
- Use the Aspire CLI from PowerShell (`aspire start --non-interactive`, `aspire describe`, `aspire stop`); never `dotnet run` the AppHost.
- The Ollama container's host port is assigned by Aspire and may not be 11434. Take the real endpoint from `aspire describe` or the dashboard.
- Env vars set in the shell before `aspire start` reach the AppHost's configuration. The branch documented `Model__Enabled=false` working this way.
- Reference files: `git show feat/local-remediation:src/AutoRemediator.AppHost/AppHost.cs` (this has the `Model:Enabled` shape plus the old `Git:*` switch; ignore `WithGit`, because task 05 replaces it) and `git show 5071d9f -- src tests`.
