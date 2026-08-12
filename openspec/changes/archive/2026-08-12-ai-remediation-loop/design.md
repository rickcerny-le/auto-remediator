## Context

Slice 4 left the loop's inputs sitting ready. `VerificationService.VerifyAsync` returns a `VerificationOutcome` whose `DependencyFailure` classification carries structured `VerificationDiagnostic` records (code, message, repository-relative path, line, column), and it produced those by extracting the repository into a real working tree and building it. `RemediationRunner` currently treats that classification as terminal:

```
Applying → Verifying → ─ Rejected ──▶ VerificationFailed   (no PR)
                        └ else ─────▶ Pushing → CreatingPr
```

The placeholder that this slice replaces is `MafRemediationAgent`: it holds a null `AIAgent`, logs, and returns `RemediationOutcome(Handled: false)`. Its interface takes a `RemediationRunRequested` — the queue message — which carries an org, project, repository name and run id, and none of the diagnostics or source the agent actually needs.

Two constraints shape everything below. The system must stay runnable on one machine (only the targeted Azure DevOps repository and its feed are external). And Slice 4's hard-won property must not regress: the tool never reports a change as verified unless it compiled.

## Goals / Non-Goals

**Goals:**
- Repair compile and API breaks introduced by a dependency bump, by editing source and re-verifying.
- Run the model locally in development and against Azure when deployed, with identical application code.
- Bound the loop explicitly — attempts, tokens, and time.
- Make it impossible for the agent to write outside its workspace or to push anything without a pull request.
- Disclose AI authorship on the pull request.
- Keep a run that exhausts its budget no worse off than today: `VerificationFailed`, no pull request, diagnostics recorded.

**Non-Goals:**
- Fixing restore-time (`NU`) failures. Those are version math; feed-metadata alignment owns them and source edits cannot resolve them.
- Revisiting version selection. The agent does not bump packages — if a break can only be fixed by a different version, that is out of scope and the run fails.
- Running the target repository's tests. Still CI's job.
- Letting the agent execute arbitrary commands. It proposes file edits; only `restore` and `build` run.
- Guaranteeing a fix rate, or gating CI on one.

## Decisions

### Engage only on compile diagnostics

The loop runs when a `DependencyFailure` contains compiler (`CS`) diagnostics. A restore failure is skipped without invoking the agent at all.

This mirrors the split the roadmap made deliberately when it separated Slice 3b from Slice 5: restore-time conflicts are computable from feed metadata, compile breaks need source edits. An agent handed `NU1107: Version conflict detected` would either flail or, worse, "fix" it by editing a manifest — which is the planner's job and would silently undo policy decisions. Cheaper to not ask.

### Verification becomes a session; the workspace outlives a single verify

This is the one structural change to Slice 4's code. `VerifyAsync` currently owns the workspace:

```csharp
finally { workspace?.Dispose(); }   // deletes the extracted tree
```

A loop needs the opposite: extract once, then verify repeatedly as edits accumulate, so attempt 3 compiles attempts 1 and 2 as well. The workspace lifetime therefore moves up to the runner, which owns it for the whole run, and verification becomes a callable operation against it.

Considered and rejected: re-extracting the tree per attempt and re-applying accumulated edits. It keeps `VerifyAsync` self-contained but costs a full archive download per attempt and makes edit accumulation an explicit bookkeeping problem instead of a natural consequence of editing files in place.

The `finally`-based cleanup guarantee must survive the move — the run disposes the workspace on every exit path, as it does today.

### Bounds: attempts, tokens, time — whichever trips first

| Bound | Default | Why |
|---|---|---|
| Max attempts | 3 | Compile-error repair that has not converged in three tries is usually the wrong shape of problem, not one more edit away. |
| Per-run token budget | configured | Cost ceiling that holds regardless of how cheap any single attempt looks. |
| Per-attempt timeout | configured | A model call that hangs must not hold a queue message open. |

Exhausting any bound ends the loop and the run lands in `VerificationFailed` with the diagnostics from the last attempt, the attempt count, and the transcript. That is the same terminal state as today plus evidence, so the floor never drops below Slice 4's behavior.

### The edit safety boundary

The agent returns proposed file edits; the system applies them, and refuses those that violate:

- **Inside the workspace only.** Paths resolve under the workspace root or are rejected — the same zip-slip guard Slice 4 applies to extraction.
- **Existing files only.** Consistent with Slice 4's rule that an edit to a file absent from the tree is a defect, not a file to create. This is what prevents an edit landing where nothing builds and the unedited tree compiling green.
- **No dependency manifests.** `Directory.Packages.props`, `*.csproj` version elements and lock files belong to the planner and to restore. An agent editing them would undo policy silently.
- **No verification artifacts.** The generated `NuGet.config` is not the agent's to touch and never reaches a commit.

Push safety is stated as a requirement rather than left implicit: writes go only to the per-repository `autoremediator/dependency-updates` branch, always through a pull request, never to the target or any protected branch. The existing pipeline already behaves this way; the point is that adding an agent makes it worth being explicit.

### Local AI via `RunAsFoundryLocal()`

```csharp
// AppHost — the only line that differs between local and deployed
var foundry = builder.AddFoundry("foundry").RunAsFoundryLocal();
var chat = foundry.AddDeployment("chat", FoundryModel.Local.Phi4);

builder.AddProject<Projects.AutoRemediator_Worker_Remediation>("remediation")
    .WithReference(chat).WaitFor(chat);
```

```csharp
// Agents — identical either way
builder.AddAzureAIInferenceChatClient("chat");
```

This is the same shape as `RunAsEmulator()` for Storage and Service Bus, and it is why this slice does not compromise local-first: the consuming code sees an `IChatClient` with health checks and OpenTelemetry, and never learns which side of the line it is on. MAF composes over `IChatClient`, so the agent is built from the injected client.

It also replaces a bespoke configuration pair (`Agents:FoundryEndpoint`, `Agents:ModelDeploymentName`) with a connection reference — the same cleanup applied to the Azure clients when they moved onto the Aspire integrations.

Alternative considered: `CommunityToolkit.Aspire.Hosting.Ollama`. Container-based, so it needs no separate install and reuses the existing container runtime, and it offers a wider model catalogue. Rejected for the asymmetry — it gives no single-line path to the Azure Foundry account the deployed environment already provisions, so app code or configuration would have to differ by environment. Worth revisiting if a specific model justifies it.

### Spike result: local Foundry stays local

Task 1.1 ran before any further wiring. Findings:

```
AppHost with AddFoundry("foundry").RunAsFoundryLocal()
  + foundry.AddDeployment("chat", FoundryModel.Local.Phi4)

aspire start  →  ✅ AppHost started successfully
  storage / servicebus / tables / blobs / api / web   Running · Healthy
  foundry                                             FailedToStart
  chat                                                Unknown
  remediation                                         Waiting

aspire logs foundry
  Foundry Local could not be started. Ensure it's installed correctly:
  (Error: An error occurred trying to start process 'foundry' … The system
  cannot find the file specified.)
```

Two conclusions:

1. **No Azure provisioning requirement.** The AppHost started with no subscription or location configured anywhere and produced no provisioning error. The `AddAzureProvisioning` concern does not bite when the resource runs locally, so Foundry Local is viable and the Ollama fallback is unnecessary.
2. **`FoundryModel.Local.Phi4` and the account-level deployment API are correct as written** — the AppHost compiles and models the resource. Note `using Aspire.Hosting.Foundry;` is required for `FoundryModel`, as the package's rename notes warn.

The spike also exposed the soft-dependency problem below, which is the more valuable finding.

### The model is a soft dependency

The spike's `WaitFor(chat)` on the remediation worker left it `Waiting` forever when Foundry Local was absent: no bumps, no verification, no pull requests — the entire system idle because a *repair* capability was missing.

That is the wrong failure shape, and it contradicts how the rest of the pipeline behaves. Slice 4 established the principle: when something that would *improve* a run is unavailable, degrade and still produce the pull request. An unreachable feed yields a `Skipped` verification and an unverified PR rather than no PR. The model deserves the same treatment — it repairs breaks that would otherwise end the run, so its absence should cost repairs, not the whole pipeline.

Decided: the remediation worker references the model deployment but **does not wait for it**. When the model is unavailable, a run that hits a compile break ends in `VerificationFailed` exactly as it does today without any agent, recording that remediation was unavailable. Mechanical bumps that verify cleanly are unaffected and keep opening pull requests.

This makes Foundry Local a prerequisite for *exercising the loop*, not for running the system.

### Model capability is an environment property, not a constant

A local Phi-4 and a deployed frontier model are not interchangeable at reading a changed API surface and repairing call sites. The loop is identical; the fix rate will not be.

So: success criteria are stated per environment and no test asserts a fix rate. The local model's job is to exercise the *loop* — bounds, accumulation, transcript capture, the safety boundary, the terminal states — all of which are deterministic and testable. Tests that need a specific model response use a fake `IChatClient`. Whether the deployed model is good enough at the actual repair task is a question for measurement after this ships, not an assumption baked into it.

## Risks / Trade-offs

- **A plausible-looking edit that compiles but is wrong** — the worst outcome here, because green is the signal the whole slice trusts → Not solvable by the loop, and not claimed to be. Mitigated by disclosure rather than by pretence: the pull request states that it contains AI-authored source edits, gives the attempt count, and links the full transcript, so review is informed. The repository's own CI, which runs the tests this system does not, remains the authority.

- **`AddFoundry` implicitly calls `AddAzureProvisioning`**, which wants a subscription and location — potentially requiring Azure configuration just to start locally → **Resolved by the task 1.1 spike: it does not.** `AddFoundry("foundry").RunAsFoundryLocal()` plus an account-level deployment starts with no Azure subscription or location configured anywhere, and no provisioning error appears. The Ollama fallback is therefore not needed. See "Spike result" below.

- **Foundry Local is a new machine prerequisite** → Accepted, but it changes the "clone and run" story, so it belongs in the README alongside the container runtime. Unlike a cloud dependency it can be installed once and used offline. The spike showed the failure is clear and self-describing when it is missing (`Foundry Local could not be started. Ensure it's installed correctly`), which is the good case.

- **A missing or unhealthy model must not block the rest of the system** → The spike surfaced this: with `WaitFor(chat)` on the remediation worker and Foundry Local absent, `foundry` reported `FailedToStart` and the worker sat in `Waiting` indefinitely — so no dependency updates happened at all, for a reason unrelated to dependencies. Resolved by not gating the worker on the model; see "The model is a soft dependency" below.

- **Foundry Projects are unsupported under `RunAsFoundryLocal()`** → Use account-level deployments, which is all this slice needs. Worth knowing before anyone reaches for a project-scoped feature.

- **Run duration and cost grow** — up to three model round-trips plus a build each → Bounded by design. The build cost is why attempts are capped low rather than left generous.

- **Moving the workspace lifetime out of `VerifyAsync` could regress Slice 4's cleanup guarantee** → The disposal must stay in a `finally` at the run level, and the existing test that asserts no workspace directory survives a run has to keep passing unchanged.

- **A local model may loop unproductively**, producing edits that neither fix nor break anything → The attempt cap ends it, and the transcript makes the pattern visible. If it turns out to be common, detecting a no-progress attempt (identical diagnostics twice) is the cheap next step.
