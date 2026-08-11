## Why

Slice 4 made the system able to tell that a dependency bump breaks the build, and to say exactly how: structured compiler diagnostics with file, line and column, against a materialized working tree. What it cannot do is fix them. A run that hits a compile break ends in `VerificationFailed` with no pull request, and a human picks it up from there.

That is the last gap on the roadmap. Restore-time conflicts are version math and were solved by feed-metadata alignment in Slice 3b; compile and API breaks need source edits, which is what this slice adds — a Microsoft Agent Framework agent that reads the diagnostics and the surrounding source, edits the call sites, and re-verifies, until the build is green or a budget is exhausted.

The loop is built on **local AI**. Aspire models a Foundry account that runs locally during development and against Azure when deployed, so the app code is identical either way and the whole system — including the AI loop — stays runnable on one machine.

## What Changes

- **New**: an iterative remediation loop. When verification rejects a change with *compile* diagnostics, the agent is given the diagnostics and the relevant source, proposes file edits into the existing working tree, and verification re-runs. This repeats until the build is green or a bound is hit.
- **New**: the loop engages **only on compile diagnostics**. A restore-time failure (`NU`-coded) is version math that source edits cannot fix, so those still end the run without invoking the agent.
- **New**: explicit bounds, settling the roadmap's open questions — a maximum attempt count, a per-run token budget, and a per-attempt timeout. Whichever trips first ends the loop.
- **New**: a hard safety boundary. The agent may only write files inside the run's workspace; it may not escape the workspace root, edit dependency manifests (the planner owns those), or touch verification-only artifacts. It proposes edits — it does not execute arbitrary commands.
- **New**: the AI transcript (prompts, proposed edits, per-attempt verification results) is captured as a run artifact alongside the verification logs.
- **MODIFIED**: local verification becomes re-runnable against one workspace. Today `VerifyAsync` disposes the workspace when it returns; the loop needs the extracted tree to survive across attempts so each attempt builds on the last.
- **MODIFIED**: the run state machine gains a `Remediating` stage between `Verifying` and `Pushing`, and records how many attempts were made.
- **MODIFIED**: the pushed commit carries the agent's source edits alongside the manifest and lock-file changes, and the pull request **discloses that it contains AI-authored source edits**, with the attempt count and a link to the transcript. A reviewer must never discover that by reading the diff.
- **MODIFIED**: the AppHost models a Foundry resource with `RunAsFoundryLocal()` and an account-level model deployment, referenced by the remediation worker. Local development uses a local model; the deployed environment uses the provisioned Azure Foundry account. Only the AppHost differs.
- **MODIFIED**: the `Agents` project consumes an injected `IChatClient` from the Aspire Azure AI Inference client integration instead of reading a bespoke `Agents:FoundryEndpoint` / `Agents:ModelDeploymentName` pair, and the `MafRemediationAgent` placeholder becomes a real agent. **BREAKING** for the deployed environment's configuration: the Foundry connection is now supplied as an Aspire connection reference rather than those two environment variables.
- **MODIFIED**: `IRemediationAgent` currently takes only a `RemediationRunRequested`, which carries none of the information the agent needs. It takes the diagnostics and the workspace instead, and returns the edits it proposes.

## Capabilities

### New Capabilities
- `ai-remediation-loop`: invoking the agent on compile diagnostics, the attempt/re-verify loop and its bounds, the edit safety boundary, transcript capture, and how the local and deployed model are selected.

### Modified Capabilities
- `local-verification`: verification is re-runnable against a single materialized workspace rather than owning and disposing it per call, so successive remediation attempts compile the accumulated edits.
- `run-orchestration`: the state machine gains a `Remediating` stage and records the attempt count; a loop that exhausts its bounds still ends in `VerificationFailed`.
- `pull-request-authoring`: the commit includes the agent's source edits, and the pull-request description discloses AI-authored edits with the attempt count and transcript link.
- `run-observability-ui`: run detail exposes the attempt count and the AI transcript, and the transcript is retrievable like the verification log.
- `service-orchestration`: the AppHost models a Foundry resource that runs locally in development, with the remediation worker referencing its model deployment.
- `azure-test-environment`: the Foundry endpoint and deployment reach the remediation job as an Aspire connection reference rather than as `Agents:FoundryEndpoint` and `Agents:ModelDeploymentName`.
- `solution-scaffold`: the `Agents` project is no longer a placeholder making no model calls, and is configured from an injected chat client rather than from its own configuration section.

## Impact

**Code**
- `src/AutoRemediator.Agents/` — `MafRemediationAgent` becomes a real MAF agent over an injected `IChatClient`; `IRemediationAgent` signature changes; `AgentsOptions` loses the endpoint/deployment settings and gains the loop bounds; `AgentsExtensions` registers the chat client.
- `src/AutoRemediator.Infrastructure/Verification/` — the workspace lifetime moves out of `VerificationService` into a session the runner owns; verification becomes callable repeatedly.
- `src/AutoRemediator.Infrastructure/Remediation/RemediationRunner.cs` — the loop between `Verifying` and `Pushing`; the agent's edits join the change set; the PR description gains the AI disclosure.
- `src/AutoRemediator.Domain/` — `RunStatus.Remediating`; attempt count and transcript reference on the run.
- `src/AutoRemediator.AppHost/AppHost.cs` — the Foundry resource and its local-run configuration.
- API, DTOs and the run detail page — attempt count and transcript.
- `infra/terraform/` — the remediation job's Foundry environment variables become a connection reference.

**Dependencies and prerequisites**
- `Aspire.Hosting.Foundry` in the AppHost and `Aspire.Azure.AI.Inference` in the consuming project. Note the package rename: this integration was previously `Aspire.Hosting.Azure.AIFoundry`, and `AIFoundryModel` is now `FoundryModel`.
- **Foundry Local must be installed on the development machine.** It runs locally, but it is a new prerequisite alongside the container runtime, and the AppHost manages its lifecycle rather than provisioning it.
- Run duration grows by the model round-trips and one extra build per attempt.

**Not affected**
- Analysis, policy and family alignment. The loop consumes their output and never revisits version selection.
- Restore-time failure handling, which continues to end the run without the agent.
- The environment-skip degradation path: a run that could not verify still opens its pull request unverified, and never invokes the agent.
