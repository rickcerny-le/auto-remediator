# Capability specs

One file per capability, describing **what is actually built** — the durable record
that Principle III of the [constitution](../../.specify/memory/constitution.md)
requires. These are present-tense descriptions of the system as it exists, not plans.

These specs were authored under OpenSpec, one capability at a time, as each slice of
the [roadmap](../../ROADMAP.md) landed. OpenSpec has since been retired in favour of
Spec Kit (`/speckit-specify` → `/speckit-plan` → `/speckit-tasks` →
`/speckit-implement`); the specs themselves were kept because they describe the
system and the code alone does not.

## Keeping them true

When a spec and the code disagree, one of them is wrong and both cannot stay that
way. Per Principle III, the spec is corrected — a spec describing something that was
never built is worse than no spec. Update the affected file in the same pull request
as the behaviour change, not afterwards.

New work is specified under Spec Kit on its own branch. When it merges, fold what it
actually built into the capability file(s) it touched, adding a new file only for a
genuinely new capability.

## Capabilities

| Capability | Covers |
| --- | --- |
| [solution-scaffold](solution-scaffold.md) | Project layout, reference topology, inward-facing dependencies |
| [testing-foundation](testing-foundation.md) | xUnit v3 + Playwright test topology and the definition-of-done gate |
| [service-orchestration](service-orchestration.md) | Aspire AppHost, emulators, injected connection information |
| [target-configuration](target-configuration.md) | Enrolled repos, package patterns, feeds, policy |
| [azure-devops-connectivity](azure-devops-connectivity.md) | Reading manifests from and pushing branches to Azure DevOps |
| [dependency-analysis](dependency-analysis.md) | Matching package patterns and resolving the outdated set |
| [update-policy](update-policy.md) | Patch/minor/major strategy, ignores, target branch |
| [update-execution](update-execution.md) | Applying version bumps to manifests |
| [dependency-graph-alignment](dependency-graph-alignment.md) | Feed-metadata alignment; restore-time (`NU`) conflicts |
| [local-verification](local-verification.md) | Restore and build in a temporary workspace |
| [ai-remediation-loop](ai-remediation-loop.md) | Bounded agent loop, edit safety boundary, transcripts |
| [pull-request-authoring](pull-request-authoring.md) | Branch, commit, PR body, AI-edit disclosure |
| [run-orchestration](run-orchestration.md) | Scheduler enqueue, queue consumption, run lifecycle |
| [run-observability-ui](run-observability-ui.md) | Run status, history, logs, diffs, transcripts |
| [change-review-gate](change-review-gate.md) | Holding an AI-repaired change for approval; the four review commands; staleness; held-repository visibility |
| [web-ui-foundation](web-ui-foundation.md) | Blazor Web App shell and interactivity model |
| [managed-identity-auth](managed-identity-auth.md) | Managed identity to Azure; no credentials in source |
| [terraform-iac](terraform-iac.md) | Modular Terraform, budget-SKU test environment |
| [azure-test-environment](azure-test-environment.md) | What the test environment provisions |
| [container-images](container-images.md) | Per-service Dockerfiles |
