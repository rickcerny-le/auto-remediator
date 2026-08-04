## ADDED Requirements

### Requirement: Trunk-based triggers
The pipeline SHALL treat `main` as trunk: it SHALL run a validation build on pull requests targeting `main`, and a CI/CD build on commits merged to `main`. No long-lived release branches SHALL be required.

#### Scenario: PR opens a validation build
- **WHEN** a pull request targeting `main` is opened or updated
- **THEN** the validation build runs

#### Scenario: Merge to trunk runs CI/CD
- **WHEN** a commit lands on `main`
- **THEN** the CI/CD build runs (build, test, push images, deploy)

### Requirement: PR validation build
The PR validation build SHALL restore, build, and run the full test suite; build all four service images to prove they build **without pushing** them; and run `terraform validate` against the test environment. It SHALL NOT push images or deploy.

#### Scenario: Validation build proves images build but pushes nothing
- **WHEN** the validation build runs for a pull request
- **THEN** it compiles, all tests pass, all four images build successfully, `terraform validate` succeeds, and no image is pushed and no deployment occurs

#### Scenario: Failing tests block the PR
- **WHEN** the test suite fails during a validation build
- **THEN** the validation build fails, signalling the branch policy to block the pull request

### Requirement: Trunk CI/CD build pushes images
On merge to trunk, the pipeline SHALL build and push all four service images to ACR, each tagged with the commit short SHA and `latest`, using an Azure service connection with push rights. Image push SHALL NOT occur on pull-request builds.

#### Scenario: Trunk build pushes tagged images
- **WHEN** the CI/CD build runs on `main`
- **THEN** all four images are pushed to ACR with the commit short-SHA and `latest` tags

#### Scenario: Push and deploy are gated to trunk
- **WHEN** the build reason is a pull request
- **THEN** the push and deploy stages are skipped

### Requirement: Deploy stage updates the test environment
After images are pushed, a deploy stage SHALL update the two Container Apps (`api`, `web`) and the two Container Apps Jobs (`scheduler`, `remediation`) in the test environment to the newly pushed commit-tagged images.

#### Scenario: Deploy rolls out the new images
- **WHEN** the deploy stage runs after a successful push
- **THEN** each of the four Container Apps/Jobs is updated to reference the image tagged with the current commit short SHA

### Requirement: Parameterized environment configuration
Environment-specific values — ACR login server, resource group, Azure service connection, and the ACA app/job resource names — SHALL be supplied via pipeline variables (or a variable group), not hard-coded in step logic, so the same pipeline can target another environment by changing variables.

#### Scenario: Targeting is variable-driven
- **WHEN** the pipeline is inspected
- **THEN** the registry, resource group, service connection, and resource names are referenced through variables rather than literals embedded in tasks

### Requirement: Reusable, documented pipeline structure
Build, image-build/push, and deploy logic SHALL live in reusable step templates invoked by the top-level pipeline, and the one-time Azure DevOps setup (service connection, variable group, branch policy) SHALL be documented in a pipeline README.

#### Scenario: Templates are reused across stages
- **WHEN** the pipeline definition is inspected
- **THEN** build/test, docker build(+push), and deploy steps are defined once as templates and referenced by the PR and trunk paths

#### Scenario: Setup is documented
- **WHEN** the pipeline README is read
- **THEN** it describes the required service connection, variable group/values, and the branch policy that gates PRs on the validation build
