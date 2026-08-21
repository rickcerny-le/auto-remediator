# terraform-iac Specification

## Purpose

Defines the Terraform infrastructure-as-code conventions for the AutoRemediator solution: a modular layout separating reusable modules from environment roots, pinned provider and Terraform versions, explicit state handling, consistent naming and tagging, and a reproducible, documented plan/apply workflow.

## Requirements

### Requirement: Modular Terraform layout
The IaC SHALL live under `infra/terraform/` and separate reusable modules from environment roots: reusable resource modules under `infra/terraform/modules/<module>/`, and one composable root per environment under `infra/terraform/environments/<env>/`. An environment root SHALL declare no resources directly except by calling modules and provider/state configuration.

#### Scenario: Environment root composes only modules
- **WHEN** `infra/terraform/environments/test/` is inspected
- **THEN** its resource creation is expressed through `module` blocks (plus provider, version, and variable declarations), and reusable resources live under `infra/terraform/modules/`

#### Scenario: A module is self-contained and reusable
- **WHEN** any directory under `infra/terraform/modules/` is inspected
- **THEN** it exposes typed input variables and outputs, contains no environment-specific hard-coded values, and can be instantiated more than once where the resource is inherently multi-instance (e.g. the container-app and container-apps-job modules)

### Requirement: Provider and version pinning
Every Terraform root SHALL pin the required Terraform version and provider versions in a `versions.tf` (or equivalent `required_providers` block), and providers SHALL be configured in the environment root, not in modules.

#### Scenario: Versions are pinned
- **WHEN** an environment root is initialized
- **THEN** `terraform init` resolves pinned `required_version` and `required_providers` constraints (at minimum the `azurerm` and `random` providers), and modules declare provider requirements but do not configure provider credentials

### Requirement: State handling is explicit
The test environment SHALL use local Terraform state, and the state choice SHALL be documented in `infra/terraform/README.md` including how to migrate to a remote backend later.

#### Scenario: State approach is documented
- **WHEN** `infra/terraform/README.md` is read
- **THEN** it states that the test environment uses local state and describes the path to a remote (azurerm) backend for later environments

### Requirement: Consistent naming and tagging
Resource names and tags SHALL be derived from shared inputs (at minimum a project prefix and environment name), and globally-unique resource names (e.g. storage account, container registry) SHALL incorporate a deterministic uniqueness suffix. All top-level resources SHALL carry common tags including the environment.

#### Scenario: Names and tags are derived, not ad hoc
- **WHEN** the planned resources are inspected
- **THEN** their names follow the shared prefix/environment convention with a uniqueness suffix where Azure requires global uniqueness, and each carries the common tag set (including `environment`)

### Requirement: Plan/apply workflow is reproducible and documented
A clean checkout SHALL be able to `terraform init` and `terraform plan` the test environment given a variables file, and the workflow SHALL be documented in `infra/terraform/README.md`.

#### Scenario: Init and plan succeed from a clean checkout
- **WHEN** an operator runs `terraform init` then `terraform plan` in `infra/terraform/environments/test/` with the documented variables supplied
- **THEN** Terraform produces a valid plan with no configuration or validation errors
