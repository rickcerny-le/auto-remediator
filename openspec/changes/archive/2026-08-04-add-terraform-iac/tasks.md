## 1. Terraform skeleton and conventions

- [x] 1.1 Create `infra/terraform/` with `modules/` and `environments/test/` directories
- [x] 1.2 Add `environments/test/versions.tf` pinning Terraform `>= 1.9` and providers `azurerm ~> 4` and `random ~> 3`
- [x] 1.3 Add `environments/test/providers.tf` (azurerm `features {}`, `subscription_id` via variable/env) — providers configured only in the root
- [x] 1.4 Add root `variables.tf` (project prefix, environment, location=eastus2, subscription_id, per-component image vars, cron expression, KEDA queue threshold, model name/capacity) and `terraform.tfvars` with test defaults
- [x] 1.5 Add root `locals.tf` deriving names/tags from prefix+environment, with a `random_string` uniqueness suffix for globally-unique names

## 2. Foundational modules

- [x] 2.1 `modules/resource-group` — resource group with common tags
- [x] 2.2 `modules/log-analytics` — Log Analytics workspace (PerGB2018, low retention)
- [x] 2.3 `modules/identity` — user-assigned managed identity + inputs for target resource ids to assign roles against
- [x] 2.4 `modules/container-registry` — Azure Container Registry (Basic)

## 3. Backing service modules

- [x] 3.1 `modules/storage` — Storage account (Standard LRS) with a Table service table and a Blob container; output table + blob endpoints
- [x] 3.2 `modules/service-bus` — Service Bus namespace (Basic) with the `remediation-runs` queue; output namespace FQDN and queue name
- [x] 3.3 `modules/ai-foundry` — AI Services account (kind `AIServices`, S0) + `gpt-4o-mini` `azurerm_cognitive_deployment`; output endpoint and deployment name

## 4. Compute modules

- [x] 4.1 `modules/container-apps-environment` — Container Apps Environment (Consumption) wired to the Log Analytics workspace
- [x] 4.2 `modules/container-app` — reusable app module: image var, external ingress toggle, min/max replicas (scale-to-zero), attached user-assigned identity, ACR pull via that identity, env-var map input
- [x] 4.3 `modules/container-apps-job` — reusable job module supporting a `Schedule` (cron) trigger and an `Event` trigger with a Service Bus queue-length KEDA scale rule; attached identity; env-var map input

## 5. RBAC wiring (Managed Identity access model)

- [x] 5.1 In `modules/identity` (or a `role-assignments` submodule), assign to the MI: Storage Table Data Contributor + Storage Blob Data Contributor (storage scope)
- [x] 5.2 Assign Azure Service Bus Data Sender + Data Receiver (namespace scope)
- [x] 5.3 Assign AcrPull (registry scope) and Cognitive Services OpenAI User (AI Services scope)

## 6. Environment composition (environments/test)

- [x] 6.1 In `environments/test/main.tf`, instantiate resource-group, log-analytics, identity, container-registry, storage, service-bus, ai-foundry, and container-apps-environment
- [x] 6.2 Instantiate `container-app` for `api` and `web` (external ingress), passing Storage/Service Bus endpoint env vars and the shared identity
- [x] 6.3 Instantiate `container-apps-job` for `scheduler` (cron trigger) with Service Bus env vars and identity
- [x] 6.4 Instantiate `container-apps-job` for `remediation` (event trigger + Service Bus KEDA scale rule) with Storage/Service Bus env vars, `Agents__FoundryEndpoint` + `Agents__ModelDeploymentName`, and identity
- [x] 6.5 Ensure RBAC role assignments depend on both the identity and their target resources so apply ordering is correct
- [x] 6.6 Add `environments/test/outputs.tf` (ACR login server, app FQDNs, storage/service-bus/foundry endpoints, MI client id)

## 7. Documentation

- [x] 7.1 Add `infra/terraform/README.md`: prerequisites (Terraform, `az login`/subscription), the `init`/`plan`/`apply` workflow, how to supply real image references, the local-state note + azurerm-backend migration path, and the required follow-up app change (endpoints + `DefaultAzureCredential`) before deployed apps can authenticate

## 8. Verification

- [ ] 8.1 Run `terraform fmt -recursive` and confirm formatting is clean *(not run — user asked not to run Terraform this session; run locally)*
- [ ] 8.2 Run `terraform init` in `environments/test/` and confirm providers/modules resolve *(not run — pending user)*
- [ ] 8.3 Run `terraform validate` in `environments/test/` and confirm zero errors *(not run — pending user)*
- [ ] 8.4 Run `terraform plan` against an authenticated subscription and confirm a valid plan (requires `az login` + subscription with rights) *(not run — pending user)*
