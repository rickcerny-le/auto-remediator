<!--
Sync Impact Report
==================
Version change: (unversioned template) -> 1.0.0

Rationale: initial ratification. The file previously contained only unresolved
[PLACEHOLDER] tokens, so this is the first substantive constitution rather than an
amendment. MAJOR/MINOR/PATCH accounting begins at 1.0.0.

Principles defined (all new):
  - I. Test-First (NON-NEGOTIABLE)
  - II. Local-First Runnability
  - III. Spec-Driven Trunk-Based Delivery
  - IV. Everything as Code
  - V. Honest Systems
  - VI. Dependencies Flow Inward

Sections added:
  - Technology and Architecture Constraints (replaces [SECTION_2_NAME])
  - Development Workflow and Quality Gates (replaces [SECTION_3_NAME])
  - Governance (populated)

Sections removed: none. The template's five generic principle slots were expanded to
six named principles; no template section was dropped.

Follow-up TODOs:
  - TODO(WARNINGS_AS_ERRORS): Principle I and the Quality Gates section require
    warnings-as-errors, but the repository currently has no `Directory.Build.props`
    and no `TreatWarningsAsErrors` setting. Add one before this gate can be enforced.
-->

# AutoRemediator Constitution

AutoRemediator is an automated dependency-remediation agent for Azure DevOps
repositories: a .NET 10 / Aspire distributed application that bumps dependencies,
verifies them, repairs the breaks an AI loop can repair, and opens pull requests.
This document governs how it is built. It supersedes habit, convenience, and
individual preference.

## Core Principles

### I. Test-First (NON-NEGOTIABLE)

Every change to C# behavior begins with a failing test. Red -> Green -> Refactor, in
that order, with no exceptions negotiated after the fact.

- A test that has never failed proves nothing and MUST NOT be counted as coverage.
- Implementation code MUST NOT be written before a test that fails for the intended
  reason exists and has been observed failing.
- Bug fixes MUST start with a test that reproduces the bug.
- Terraform is verified differently: `terraform fmt`, `validate`, and human review of
  `plan` output gate IaC changes. Test-first is NOT required for IaC.
- The Blazor Web UI is verified by Playwright functional tests. Those tests are
  required, but need not be written before the page they exercise.
- No numeric coverage floor is imposed. Coverage is a consequence of practicing
  test-first, not a target to satisfy; a threshold would only invite tests written for
  the metric.

Rationale: this system edits other engineers' source code and opens pull requests
against their repositories. The cost of a silent behavioral regression is paid by
someone who did not write the bug. Tests written first are the only ones that
reliably describe intent rather than reproduce whatever the implementation happens
to do.

### II. Local-First Runnability

A developer handed this repository MUST be able to run every step of the system on
their own machine, with no Azure subscription, on the day they clone it.

- `aspire start` MUST bring up the entire application: API, Web, both workers, and
  their dependencies. Nothing may require a cloud resource to boot.
- Azure Storage, Service Bus, and the chat model MUST be satisfiable by local
  substitutes (Azurite, the Service Bus emulator, Foundry Local). Connection
  information MUST be injected by the AppHost; connection strings MUST NOT be
  hard-coded in project code.
- The only permitted non-local dependencies are the **target** Azure DevOps
  repository and NuGet feed that a run remediates. These are the subject of the tool,
  not part of it.
- Infrastructure is exercised locally as `terraform validate` and `terraform plan`.
  `terraform apply` against a real subscription is a deployment step, not a local
  development step, and no local workflow may depend on it.
- A dependency that cannot be made local MUST degrade rather than block. Absent
  Foundry Local, the system still runs and still opens pull requests; only AI repair
  is unavailable, and the run MUST record that fact.
- Every command a developer needs MUST be in the README, copy-pasteable, and correct.
  A README instruction that does not work as written is a defect.

Rationale: this project is meant to be handed off. Onboarding friction that requires
a subscription, a ticket, or a conversation is friction that compounds against every
future contributor.

### III. Spec-Driven Trunk-Based Delivery

Work is specified before it is built, and `main` is always releasable.

- Every unit of work is a spec (an OpenSpec change or Spec Kit feature) with its own
  short-lived branch. No spec, no branch.
- `main` is the single trunk. Branches MUST be short-lived — measured in days, not
  weeks — and MUST rebase or merge from `main` rather than diverge.
- Every branch merges into `main` through a pull request, **squashed to one commit**,
  with CI green. The branch is deleted on merge.
- Direct commits to `main` are prohibited, including for documentation, configuration,
  and typo fixes. There is no size threshold that exempts a change from review.
- Specs are the durable record. When implementation diverges from the spec, the spec
  is corrected — a spec that describes something that was never built is worse than
  no spec.
- Long-lived feature branches, release branches, and merge trains are out of scope for
  this repository.

Rationale: one squashed commit per spec makes `main` a readable list of capabilities
and makes any change trivially revertible. Trunk-based flow keeps integration pain
continuous and small instead of deferred and large.

### IV. Everything as Code

Every deployable and reproducible aspect of this system lives in the repository.

- All Azure infrastructure is declared in Terraform under `infra/terraform/`.
  Resources MUST live in reusable modules; environment roots MUST contain only
  providers, versions, variables, and `module` blocks.
- Changes made through the Azure Portal or ad-hoc CLI are configuration drift, not
  changes. Anything discovered to exist outside Terraform MUST be either imported or
  destroyed.
- CI/CD is declared in `azure-pipelines.yml` and its templates. Pipeline behavior
  MUST NOT depend on manually configured build steps.
- Container images are built from Dockerfiles committed alongside each service.
- Secrets MUST NOT be committed. Authentication to Azure uses managed identity;
  credentials in source, in Terraform variable files, or in pipeline literals are
  defects.

Rationale: an environment that can only be reproduced by remembering what someone
clicked is not reproducible. IaC is what makes a second environment — and a handoff —
possible.

### V. Honest Systems

The system, its tests, and its documentation MUST describe what is actually true.

- Tests MUST NOT assert properties of a language model. The AI loop's mechanics
  (bounds, edit safety, terminal states, transcripts) are deterministic and MUST be
  fully tested against a fake chat client; repair *rate* MUST NOT be asserted,
  because it is a property of the model, not of this system.
- Documentation MUST state what is not built yet, and MUST distinguish local behavior
  from deployed behavior wherever they differ.
- Every run MUST be observable: structured logs, OpenTelemetry traces through
  `ServiceDefaults`, health checks, and a durable record of status, diffs, and AI
  transcripts.
- A pull request containing AI-authored edits MUST say so, with the attempt count and
  a link to the transcript. Reviewers MUST be able to tell which lines a model wrote.
- The agent proposes edits; it never executes commands. Only `restore` and `build`
  run. Edits are refused unless they target an existing file inside the run's
  temporary tree; dependency manifests, project files, lock files, and verification
  artifacts are off limits. Nothing is pushed outside a pull request on the
  `autoremediator/dependency-updates` branch.

Rationale: this tool asks other engineers to trust machine-authored changes to their
code. That trust is only warrantable if the system never overstates what it did.

### VI. Dependencies Flow Inward

The solution's reference topology is a constraint, not a suggestion.

- `Domain` and `Contracts` depend only on `Shared`.
- `Infrastructure` and `Agents` depend on `Domain`, `Contracts`, and `Shared`.
- Hosts (`Api`, `Web`, both workers) depend on `Infrastructure`, `Contracts`, and
  `ServiceDefaults`; the remediation worker also depends on `Agents`.
- No library may reference a host. No inward-facing project may reference an
  outward-facing one.
- The API is a vertical slice: endpoints delegate to handlers directly, with no
  mediator indirection.

Rationale: the domain is the part of this system worth keeping. Keeping it free of
transport, storage, and model concerns is what allows it to be tested first and
cheaply.

## Technology and Architecture Constraints

- **Runtime**: .NET 10. **Orchestration**: .NET Aspire 13.4.x, driven by the Aspire
  CLI (`aspire start` / `aspire stop` / `aspire describe`). `dotnet run` on the
  AppHost is prohibited — it bypasses lifecycle management and orphans processes that
  hold file locks on `bin/` and `obj/` (surfacing as `MSB3491` / `CS2012`).
- **Tests**: xUnit v3 for unit and integration tests; Playwright for Web functional
  tests. API integration tests MUST exercise the real host through a test server, not
  a mocked HTTP pipeline.
- **Cloud**: Azure. Container Apps for `api` and `web`; Container Apps Jobs for
  `scheduler` (cron, run-once-then-exit) and `remediation` (Service Bus queue-scaled
  via KEDA). Deployed worker shape MUST NOT be inferred from the local project-host
  shape.
- **IaC**: Terraform >= 1.9, budget SKUs for the test environment.
- **CI/CD**: Azure Pipelines. A pull request into `main` validates (build, test, image
  build without push, `terraform validate`); a merge to `main` packages and deploys.
- **AI**: Microsoft Agent Framework. Foundry Local in development, an Azure AI
  Foundry deployment when deployed. The chat connection is injected as
  `ConnectionStrings:chat`; its absence means no AI repair, never a failure to run.
- **Every testable project carries at least one test**, so `dotnet test` exercises the
  whole solution.

## Development Workflow and Quality Gates

Order of work for any change:

1. Create or update the spec; open a branch named for it (e.g.
   `slice-4-local-verification`).
2. Write the failing test.
3. Implement until green. Refactor.
4. Run the full local gate.
5. Open a pull request; squash-merge on green CI; delete the branch.
6. Sync the spec to reflect what was actually built, then archive the change.

A branch MUST NOT merge unless all of the following hold:

- `dotnet build AutoRemediator.sln` succeeds with **no new warnings**, with warnings
  treated as errors.
- `dotnet test AutoRemediator.sln` is fully green. A skipped test MUST be skipped for
  a stated, mechanical reason (for example, the Playwright suite self-skipping without
  `WEB_BASE_URL`), never to sidestep a failure.
- `terraform fmt -check` and `terraform validate` pass for every touched module, and
  the `plan` for any infrastructure change has been read by a human.
- The README and the affected specs are accurate as of the merge.
- New or changed behavior is covered by a test that was observed failing first.

## Governance

This constitution supersedes all other development practices in this repository.
Where a tool default, a template, or a habit conflicts with it, this document wins.

**Amendment procedure.** Amendments are proposed as a pull request that changes this
file, states the rationale, and sets the new version. An amendment that invalidates
existing code or infrastructure MUST include a migration plan in the same pull
request. Amendments follow the same trunk-based flow as any other change: branch,
review, squash-merge.

**Versioning policy.** Semantic versioning applies to this document:

- **MAJOR** — a principle is removed, or redefined in a way that makes previously
  compliant work non-compliant.
- **MINOR** — a principle or section is added, or existing guidance is materially
  expanded.
- **PATCH** — clarification, wording, or typo fixes with no change in meaning.

**Compliance review.** Every pull request review MUST verify compliance with these
principles, not merely correctness of the diff. Complexity that appears to violate a
principle MUST be justified in the pull request description; an unjustified violation
is grounds to reject. Violations discovered after merge are tracked as defects and
fixed, not grandfathered. Runtime development guidance lives in `README.md`,
`ROADMAP.md`, and the specs under `openspec/specs/`; where those documents disagree
with this one, this one governs and they are corrected.

**Version**: 1.0.0 | **Ratified**: 2026-08-21 | **Last Amended**: 2026-08-21
