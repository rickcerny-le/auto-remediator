## Why

The solution scaffold runs locally under Aspire against emulators, but there is no way to deploy it to Azure. We need Infrastructure-as-Code so the system can be stood up in a real Azure subscription — starting with a low-cost **test** environment — reproducibly and in source control. Terraform gives us a modular, reviewable definition of the Azure footprint that maps 1:1 to the app components already built (API, Web, scheduler Job, remediation Job, Storage, Service Bus, Foundry).

## What Changes

- Add a modular **Terraform** codebase under `infra/terraform/` with reusable `modules/` and a per-environment `environments/test/` root that composes them.
- Provision the Azure footprint for the app on **budget SKUs** intended for testing:
  - **Container Apps Environment** (Consumption workload profile, scale-to-zero) backed by a **Log Analytics** workspace.
  - Two **Container Apps** with external ingress: `api` and `web`.
  - Two **Container Apps Jobs**: `scheduler` (scheduled/cron trigger, run-once) and `remediation` (event trigger, Service Bus queue-depth KEDA scaling) — matching the app's deployment model.
  - **Azure Container Registry** (Basic) for the app images.
  - **Storage account** (Standard LRS) with a Table service + Blob container.
  - **Service Bus** namespace (**Basic** SKU) with the `remediation-runs` queue.
  - **Azure AI Foundry** access via an **AI Services** account with a **gpt-4o-mini** model deployment (per-token billing, near-zero idle) for the MAF agent.
  - A **user-assigned Managed Identity** with **RBAC** role assignments (Storage Table/Blob data, Service Bus send/receive, AcrPull, Cognitive Services OpenAI User). No secrets or Key Vault.
- Wire resource **endpoints** (and the Foundry endpoint/model name) into the Container Apps as environment variables, and attach the managed identity to every app/job and to the registry pull.
- Parameterize container **image references** so `terraform apply` succeeds before the first app image is pushed (public placeholder image as the default), then real ACR images are supplied per app.
- Use **local Terraform state** for now, with a documented `apply` workflow in `infra/terraform/README.md`.

Non-goals / explicit dependencies (deferred): remote state backend, a CD pipeline to run Terraform, VNet/private networking, Key Vault, production environment and autoscale tuning, and the **application-code change** to construct Azure clients from endpoints + `DefaultAzureCredential` instead of connection strings (required before the deployed apps can actually authenticate via Managed Identity — see Impact).

## Capabilities

### New Capabilities
- `terraform-iac`: Terraform conventions for this repo — modular layout (`modules/` + `environments/`), provider/version pinning, state handling, naming/tagging, and module reusability so new environments and resources compose cleanly.
- `azure-test-environment`: The concrete Azure footprint for the test environment — which resources are provisioned, the budget SKUs used, how the ACA apps/jobs map to the app components, and the Managed-Identity + RBAC access model with endpoints wired into the apps.

### Modified Capabilities
<!-- None — this introduces new IaC capabilities; it does not change existing solution-scaffold / service-orchestration / testing-foundation requirements. -->

## Impact

- New top-level `infra/terraform/` tree (modules + `environments/test/`); no application source is modified by this change.
- Introduces a Terraform toolchain dependency (Terraform CLI, `azurerm`/`random` providers) and an Azure subscription with rights to create the above resources.
- **Application dependency:** the deployed apps authenticate via Managed Identity, but `AutoRemediator.Infrastructure` currently builds `TableServiceClient`/`BlobServiceClient`/`ServiceBusClient` from connection strings. A follow-up app change must switch these to endpoint + `DefaultAzureCredential` (and the Agents client to the Foundry endpoint) before the deployed system can connect. The Terraform here provisions the target (identity, roles, endpoint env vars) so that change is a code-only adaptation.
- Cost: intentionally minimal (Consumption ACA scale-to-zero, Basic SKUs, per-token model), but non-zero — Log Analytics ingestion, ACR Basic, and Service Bus Basic carry small standing charges.
