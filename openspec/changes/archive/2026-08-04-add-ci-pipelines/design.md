## Context

Four deployable services build and test from `AutoRemediator.sln`; the Terraform test environment provisions ACR + two Container Apps (`api`, `web`) and two Container Apps Jobs (`scheduler`, `remediation`), currently running a placeholder image. This change adds trunk-based CI/CD on **Azure DevOps** that containerizes each service (Dockerfile per service), validates every PR, and on merge to `main` pushes images to ACR and rolls them out to the test environment.

Confirmed decisions: Azure DevOps Pipelines; a Dockerfile per service; CI **and** CD (deploy to the ACA test env); PR build does build + test + image build (no push) + `terraform validate`.

## Goals / Non-Goals

**Goals:**
- One `azure-pipelines.yml` with `trigger: main` and `pr: main`, gating push/deploy to non-PR runs.
- A Dockerfile per service, built from the repo root, multi-stage on .NET 10.
- Reusable step templates (build-test, docker build[+push], deploy) so PR and trunk paths share logic.
- Commit-SHA + `latest` tagging; variable-driven environment targeting.
- Deploy stage updates the 4 ACA resources to the new images.

**Non-Goals:**
- Creating the Azure DevOps project, Azure Repos repo, ARM service connection, or variable group (one-time manual setup, documented).
- Production/staging environments, approvals/gates, semver/release notes.
- Provisioning Azure resources (Terraform owns that); the deploy stage only swaps images.

## Decisions

**1. Single multi-stage YAML, PR/trunk gated by `Build.Reason`.**
`trigger: [main]` + `pr: [main]`. Stages: `Validate` (always) → `Package` (push images) → `Deploy`, with `Package`/`Deploy` guarded by `condition: ne(variables['Build.Reason'], 'PullRequest')`. The PR run does build/test + image build (no push) + `terraform validate` inside `Validate`; trunk additionally runs `Package` and `Deploy`. Rationale: one source of truth, no drift between PR and trunk definitions. *Alternative:* two separate pipeline files — simpler conditions but duplicated logic; rejected.

**2. Dockerfile per service, repo-root context, layered restore.**
Each service project gets a `Dockerfile`. Build context is the repo root (so project references resolve). Layers: copy `AutoRemediator.sln` + the `.csproj` files first and `dotnet restore` the service project (cache-friendly), then copy the rest and `dotnet publish -c Release`. Final stage: ASP.NET Core 10 runtime for `api`/`web` (expose 8080), .NET 10 runtime for the workers (no port). Rationale: explicit control per the chosen approach, good layer caching, correct base per workload. *Trade-off:* four files to maintain vs SDK container publish — accepted per decision.

**3. Image build/push via a template with a `push` parameter.**
A `docker-build.yml` template takes `service`, `dockerfile`, `push` (bool), and `tags`. PR runs call it with `push: false`; trunk with `push: true`. Auth uses an `AzureCLI@2` task on the ARM service connection: `az acr login`, then `docker build`/`docker push`. Rationale: identical build path in both cases, push toggled by one parameter; ACR login via the service connection avoids stored registry creds. *Alternative:* `Docker@2` with a Docker Registry service connection — also fine; `AzureCLI@2` keeps a single ARM service connection for both push and deploy.

**4. Tagging: commit short SHA + `latest`.**
`$(Build.SourceVersion)` truncated to a short SHA is the immutable, traceable tag; `latest` tracks trunk head. The deploy stage references the short-SHA tag (immutable) rather than `latest`. Rationale: reproducible deploys and easy rollback by re-deploying a prior SHA.

**5. Deploy with `az containerapp (job) update`.**
Apps: `az containerapp update -n <app> -g <rg> --image <acr>/autoremediator/<svc>:<sha>`. Jobs: `az containerapp job update -n <job> -g <rg> --image ...`. Runs in a `Deploy` stage on the ARM service connection. Rationale: image-only rollout on already-provisioned resources keeps CD decoupled from Terraform (no `terraform apply` in the pipeline, avoiding state/drift coupling). *Alternative:* `terraform apply` with new image vars — couples CD to TF state and the pipeline would need state access; rejected for now.

**6. `terraform validate` in PRs without cloud access.**
`terraform fmt -check` + `terraform init -backend=false` + `terraform validate` in `infra/terraform/environments/test`. `-backend=false` avoids needing state/credentials, so validation runs on any agent. Rationale: catches IaC breakage on PRs cheaply; full `plan`/`apply` stays a deliberate manual/off-pipeline step.

**7. Variable-driven targeting.**
A variable group (e.g. `autoremediator-test`) supplies `acrLoginServer`, `resourceGroup`, `azureServiceConnection`, and the four resource names; templates read variables, never literals. Rationale: retarget another environment by swapping the variable group.

## Risks / Trade-offs

- **[No git repo / ADO project yet]** → The pipeline can't run until Azure Repos, the service connection, and the variable group exist. Mitigation: document the one-time setup in the pipeline README; the YAML/Dockerfiles are authored and ready.
- **[Deploy vs Terraform drift]** → CD updates images directly, so the ACA image no longer matches the Terraform `*_image` variable/state. Mitigation: documented — treat the running image as pipeline-owned; set the TF image vars to the same tag when reconciling, or import on next apply.
- **[Hosted-agent Docker builds are slow]** → Building four images on PRs adds minutes. Mitigation: layered Dockerfiles for caching; acceptable for a strong PR signal (the chosen option).
- **[Managed-identity vs pipeline identity]** → The pipeline pushes/deploys via the ARM service connection (not the app's managed identity). Mitigation: grant the service connection's identity AcrPush + Container Apps Contributor on the resource group; documented in the README.
- **[Short-SHA tag collisions]** → Extremely unlikely; if needed, fall back to the full SHA or `$(Build.BuildId)`.

## Migration Plan

Additive — no existing behavior changes. Roll out by committing the YAML/Dockerfiles, completing the one-time ADO setup, and enabling the branch policy. First trunk build replaces the placeholder ACA image with a real one. Rollback: re-deploy a prior commit-SHA image, or disable the pipeline.

## Open Questions

- Exact ACA resource names to target (derived from Terraform: `ca-autrem-test-api/-web`, `caj-autrem-test-scheduler/-remediation`) — supplied via the variable group; confirm at setup.
- Whether the deploy stage should later reconcile the Terraform `*_image` variables (kept out of scope now to avoid TF-state coupling).
