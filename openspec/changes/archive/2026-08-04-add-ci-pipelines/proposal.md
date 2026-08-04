## Why

The four deployable services (api, web, scheduler, remediation) build and test locally, and the Terraform test environment expects images in ACR — but there is no automated way to build, containerize, push, or deploy them. We need trunk-based CI/CD so every change is validated on a PR and, once merged to trunk, is built into per-service container images, pushed to ACR, and rolled out to the test environment.

## What Changes

- Add a **Dockerfile per service** (`AutoRemediator.Api`, `AutoRemediator.Web`, `AutoRemediator.Worker.Scheduler`, `AutoRemediator.Worker.Remediation`) — multi-stage builds from the .NET 10 SDK/runtime images, built from the repo root so shared project references resolve.
- Add an **Azure DevOps** pipeline (`azure-pipelines.yml`) plus reusable step templates, driving **trunk-based** development:
  - **PR validation** (pull requests into `main`): restore, build, run the full test suite, build all four images to prove they build (**no push**), and run `terraform validate` on the test environment.
  - **Trunk CI/CD** (merge to `main`): build + test, build and **push** all four images to ACR tagged by commit SHA and `latest`, then a **deploy** stage that updates the two Container Apps and two Container Apps Jobs in the test environment with the new images.
- Establish an **image tagging** convention (`<acr>/autoremediator/<service>:<shortsha>` + `:latest`) and pipeline variables for the ACR, resource group, service connection, and ACA resource names.
- Document the branch policy (PR-required, validation build gating) and the one-time Azure DevOps setup in a pipeline README.

Non-goals / assumptions: creating the Azure DevOps project, the Azure Repos git repo, the ARM **service connection**, and the variable group are one-time manual setup (documented, not automated); no production/staging environments or approvals; no release-notes/semver automation; provisioning of Azure resources remains Terraform's job (the deploy stage only updates images on already-provisioned resources).

## Capabilities

### New Capabilities
- `container-images`: A per-service container image contract — each deployable service has a Dockerfile that produces a runnable image from a repo-root build context, with defined base images, exposed port (apps), and entrypoint.
- `ci-cd-pipeline`: The trunk-based Azure DevOps pipeline — PR validation (build/test/image-build/terraform-validate) and trunk CI/CD (build/test/push/deploy), including tagging and stage gating.

### Modified Capabilities
<!-- None — new CI/CD capabilities; existing solution/infra/auth specs are unchanged. -->

## Impact

- Adds `azure-pipelines.yml`, pipeline step templates (under `.azuredevops/`), a Dockerfile in each of the four service projects, and a pipeline README; no application source or Terraform is modified.
- Introduces a dependency on an Azure DevOps project with Azure Repos (trunk = `main`), an ARM service connection with AcrPush + Container Apps deploy rights, and a variable group for environment-specific values.
- Consumes the ACR and ACA resources created by `add-terraform-iac`; the deploy stage supplies the real images that replace the placeholder image referenced by the Terraform.
- Cost: pipeline compute (hosted agents) per PR and per merge; image build on PRs adds time but no registry storage (no push).
