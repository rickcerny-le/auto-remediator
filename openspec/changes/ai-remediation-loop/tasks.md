## 1. Spike: does local Foundry stay local?

- [x] 1.1 Add `Aspire.Hosting.Foundry` to the AppHost and confirm whether `AddFoundry(...).RunAsFoundryLocal()` starts **without** any Azure subscription or location configured — `AddFoundry` implicitly calls `AddAzureProvisioning`, and if that demands Azure configuration at startup the local-first goal is broken
- [x] 1.2 ~~If it does demand it, evaluate `CommunityToolkit.Aspire.Hosting.Ollama` as the local path~~ — **not needed.** The spike showed no provisioning requirement; Foundry Local is viable. Result recorded in `design.md`
- [x] 1.3 Record the Foundry Local install prerequisite in the README alongside the container runtime
- [x] 1.4 Make the model a **soft** dependency — the spike's `WaitFor(chat)` left the worker idle indefinitely when Foundry Local was absent, stopping dependency updates because a repair capability was missing

## 2. AppHost and the chat client

- [x] 2.1 Model the Foundry resource with an account-level deployment and local-run configuration in `AppHost.cs`; reference it from the remediation worker but deliberately do **not** wait for it (see 1.4)
- [x] 2.2 Add `Aspire.Azure.AI.Inference` to the Agents project and register the chat client from the connection name
- [x] 2.3 Remove `FoundryEndpoint` and `ModelDeploymentName` from `AgentsOptions`; add the loop bounds (max attempts, token budget, per-attempt timeout) in their place
- [ ] 2.4 Verify the app starts with `aspire start` and every resource including the model reports healthy via `aspire describe` — blocked on 1.3 (Foundry Local installed); the non-model resources are already confirmed healthy with the model failed

## 3. Domain: attempts and transcript

- [x] 3.1 Add `Remediating` to `RunStatus`
- [x] 3.2 Record the remediation attempt count and transcript reference on `RemediationRun`, threaded through `Restore`
- [x] 3.3 Persist both in `RemediationRunStore` and extend the round-trip tests
- [x] 3.4 Unit-test the new transitions, including that an exhausted loop still lands in `VerificationFailed`

## 4. Verification becomes re-runnable

- [x] 4.1 Move the workspace lifetime out of `VerificationService.VerifyAsync` into a session the runner owns, so one tree can be verified repeatedly
- [x] 4.2 Keep cleanup unconditional at the run level — the existing test asserting no workspace directory survives a run must pass unchanged
- [x] 4.3 Test that a second verification compiles edits applied since the first, and that the archive is downloaded only once per run

## 5. The agent

- [x] 5.1 Move the agent contract into `Domain` and redefine it: it takes the diagnostics plus a narrow source-reader abstraction and returns proposed file edits, replacing the `RemediationRunRequested` parameter that carries none of what the agent needs. It cannot take `IVerificationWorkspace` — that lives in `Infrastructure`, and `Infrastructure` must call the agent, so both directions would be needed. Keeping the contract in `Domain` also keeps MAF out of `Infrastructure`'s transitive closure, which is what `solution-scaffold` requires
- [x] 5.2 Implement `MafRemediationAgent` over the injected chat client: build the prompt from the diagnostics plus the source they point at, and parse the response into proposed edits
- [x] 5.3 Report token usage per attempt so the run-level budget can be enforced
- [x] 5.4 Test the agent against a fake chat client — prompt contents, response parsing, and malformed responses

## 6. Edit safety boundary

- [x] 6.1 Apply proposed edits only when the path resolves inside the workspace root, the file already exists, it is not a dependency manifest or lock file, and it is not a verification-generated artifact
- [x] 6.2 Record every rejected edit and its reason rather than failing the attempt
- [x] 6.3 Test each rejection case: path escape, absent file, `Directory.Packages.props`, a lock file, the generated `NuGet.config`; and that a legitimate source edit is applied

## 7. The loop

- [x] 7.1 Insert the `Remediating` stage in `RemediationRunner`, entered only when verification rejected the change with compile (`CS`) diagnostics
- [x] 7.2 Loop: agent → apply permitted edits → re-verify, ending on success or when a bound trips
- [x] 7.3 On success, include the agent's source edits in the pushed change set
- [x] 7.4 On exhaustion, end in `VerificationFailed` with the final diagnostics, attempt count and transcript, and no pull request
- [x] 7.5 Test the paths end to end with a fake chat client: repaired-on-second-attempt, exhausted-after-max-attempts, budget-exhausted-midway, restore-failure-never-invokes-agent, skipped-verification-never-invokes-agent

## 8. Transcript artifact

- [x] 8.1 Capture the transcript — diagnostics presented, edits proposed with applied/rejected outcome, and each attempt's verification result — and store it as a run artifact next to the verification logs
- [x] 8.2 Ensure a failed transcript upload never changes the run's outcome
- [x] 8.3 Test that a transcript is stored for both success and exhaustion, and that rejected edits appear in it

## 9. Disclosure and observability

- [x] 9.1 Add the AI disclosure to the pull-request description: that it contains AI-authored source edits, the attempt count, and the transcript link
- [x] 9.2 Assert a run with no agent involvement makes no such claim
- [ ] 9.3 Extend the run detail DTO, endpoint and transcript route; extend `RunsEndpointTests`
- [ ] 9.4 Show attempts and the transcript link on the Run detail page

## 10. Safety and verification of the whole slice

- [x] 10.1 Assert as a test that writes go only to the per-repo update branch and always through a pull request, including on agent-repaired runs
- [ ] 10.2 Run the full test suite and confirm no regression in analysis, policy, alignment, or Slice 4's verification behavior
- [ ] 10.3 Exercise the loop locally end to end against a deliberately broken bump, using the local model, and confirm the transcript and PR disclosure read correctly
- [ ] 10.4 Document the expected repair effectiveness per environment, noting that the local development model and the deployed model differ in capability and that no test asserts a repair rate
