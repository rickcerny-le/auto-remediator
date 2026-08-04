## Context

The AutoRemediator solution is built and runs locally under Aspire against emulators. This change adds Terraform IaC to deploy it to Azure, starting with a low-cost **test** environment. The Azure footprint must map to the components already built: `api` + `web` (Container Apps), `scheduler` (scheduled/cron Job) + `remediation` (event-driven, queue-scaled Job), Storage (Tables + Blobs), Service Bus (`remediation-runs` queue), and Azure AI Foundry for the MAF agent.

Confirmed decisions: **local** Terraform state; provision a **minimal Foundry** (AI Services account + `gpt-4o-mini`) now; **user-assigned Managed Identity + RBAC** (no secrets/Key Vault); region **East US 2**; everything on the lowest viable SKUs for testing; maximum modularization.

## Goals / Non-Goals

**Goals:**
- A modular `infra/terraform/` (`modules/` + `environments/test/`) that `init`/`plan`s cleanly and deploys the full app footprint.
- Budget-first: Consumption ACA (scale-to-zero), Basic ACR, Standard LRS storage, Basic Service Bus, per-token model.
- Managed-Identity + RBAC access model; no stored connection strings/keys.
- Reusable `container-app` and `container-apps-job` modules instantiated per component.
- Deployable before app images exist (placeholder image defaults), real images swapped via variables.

**Non-Goals:**
- Remote state backend, state locking, and a CD pipeline (documented migration path only).
- VNet/private endpoints, Key Vault, WAF/Front Door.
- Production environment, autoscale/SLA tuning, cost alerts/budgets.
- The **application code change** to use `DefaultAzureCredential` + endpoints instead of connection strings (tracked as a dependency, not done here).

## Decisions

**1. Layout: `modules/` (reusable) + `environments/test/` (composition).**
Modules: `resource-group`, `log-analytics`, `container-registry`, `identity` (user-assigned MI + role assignments), `storage`, `service-bus`, `ai-foundry`, `container-apps-environment`, `container-app` (reusable), `container-apps-job` (reusable). The `environments/test/` root wires them with `versions.tf`, `providers.tf`, `variables.tf`, `terraform.tfvars`, `main.tf`, `outputs.tf`. Rationale: matches the requested "as modular as possible" and lets a future `environments/prod/` reuse every module. *Alternative:* a single flat root — simpler but not reusable; rejected.

**2. Container Apps: Consumption profile, scale-to-zero.**
One Container Apps Environment (Consumption only, no dedicated workload profile, no VNet) backed by a Log Analytics workspace. `api`/`web` are Container Apps with external ingress and `min_replicas = 0`. Rationale: cheapest ACA mode; idle cost ≈ 0. *Trade-off:* cold starts on first request — acceptable for testing.

**3. Jobs mirror the app's deployment model.**
`scheduler` → Container Apps **Job**, `Schedule` trigger with a cron `cron_expression` (default e.g. `0 */6 * * *`), `replica_timeout` bounded, run-once semantics. `remediation` → Container Apps **Job**, `Event` trigger with a `custom` KEDA scale rule of type `azure-servicebus` on `remediation-runs`. Rationale: this is exactly the scheduler=cron / remediation=queue-scaled model the app was designed for. Authentication for the KEDA scaler uses the managed identity.

**4. Managed Identity + RBAC, endpoints not secrets.**
A single user-assigned MI is attached to every app/job and used as the ACR pull identity. Role assignments: Storage `Storage Table Data Contributor` + `Storage Blob Data Contributor`; Service Bus `Azure Service Bus Data Sender` (scheduler) + `Azure Service Bus Data Receiver` (remediation) — assigned at namespace scope for test simplicity; `AcrPull` on the registry; `Cognitive Services OpenAI User` on the AI Services account. Apps get endpoints as env vars (`ConnectionStrings__tables`/`__blobs` as the *service endpoint URIs*, `ConnectionStrings__servicebus` as the namespace FQDN, `Agents__FoundryEndpoint`, `Agents__ModelDeploymentName`). Rationale: no secret sprawl, free, and the target the app should adopt. *Consequence (flagged):* the app must switch to `DefaultAzureCredential` — see Risks.

**5. Foundry via AI Services account + model deployment (not the full hub/project).**
`ai-foundry` module creates an `azurerm_cognitive_account` (kind `AIServices`, SKU `S0`) and an `azurerm_cognitive_deployment` for `gpt-4o-mini` (Standard/GlobalStandard). Rationale: this is the Foundry-family endpoint a chat/agent client connects to, and it avoids the AI Foundry *hub*'s mandatory Storage + Key Vault + App Insights dependencies — much leaner and cheaper for a test env, while still satisfying "provision Foundry." *Alternative:* `azurerm_ai_foundry` hub + project — heavier deps, deferred until the real agent needs project-scoped features.

**6. Deployable before images exist.**
Each app/job takes an `image` variable defaulting to a public placeholder (`mcr.microsoft.com/azuredocs/containerapps-helloworld:latest`) so `apply` succeeds pre-push; real images (`<acr>.azurecr.io/autoremediator/<component>:<tag>`) are supplied via tfvars later. Rationale: breaks the ACR-image-must-exist-before-app chicken/egg without a CD pipeline. Building/pushing images is out of scope (app-CI concern).

**7. Naming/tagging via locals + `random_string`.**
Root locals derive names from `project` (`autrem`) + `environment` (`test`); globally-unique names (storage, ACR) append a `random_string` suffix. Common tags (`project`, `environment`, `managed-by = terraform`) applied to all resources. Rationale: deterministic, collision-safe, no extra provider.

**8. Providers/versions.**
Terraform `>= 1.9`; `azurerm ~> 4.x` (features block, `subscription_id` via variable/env) and `hashicorp/random ~> 3.x`. Providers configured only in the environment root.

## Risks / Trade-offs

- **[App still uses connection strings]** → With MI + endpoints, the deployed apps will not authenticate until `AutoRemediator.Infrastructure` adopts `DefaultAzureCredential`. Mitigation: explicitly scoped as a follow-up app change; the IaC provisions the exact target (identity, roles, endpoint env vars) so that change is code-only. Documented in the README and proposal.
- **[Service Bus Basic limits]** → Basic supports queues (what we use) but not topics/sessions. Mitigation: fine for now; bump to Standard if topics/sessions are needed later — a one-line SKU variable change.
- **[KEDA Service Bus scaler auth]** → The event-job scaler must authenticate to Service Bus, via the managed identity. Mitigation: wire the scaler's identity/auth to the user-assigned MI; verify the queue-length rule triggers scaling in a smoke test.
- **[Local state]** → No locking or sharing; risk of drift/loss. Mitigation: documented as test-only with a clear azurerm-backend migration path; treat the test env as reproducible/disposable.
- **[Placeholder images]** → A freshly applied environment runs hello-world, not the app, until real images are pushed. Mitigation: image variables + README steps; intended behavior for an infra-first change.
- **[Foundry region/model availability]** → `gpt-4o-mini` capacity/SKU varies by region. Mitigation: East US 2 chosen for availability; model name/version and capacity are variables.

## Migration Plan

Greenfield IaC — nothing to migrate. Roll-forward: `terraform apply` in `environments/test/`. Roll-back/teardown: `terraform destroy` (test env is disposable). Future: migrate state to an `azurerm` backend and add `environments/prod/` reusing the same modules.

## Open Questions

- Exact `azurerm` minor version and the `gpt-4o-mini` model version/capacity to pin (resolve at apply against provider + region availability).
- Default cron cadence for the scheduler Job and the KEDA queue-length threshold for remediation (sensible defaults now, tune later).
- Whether to add a minimal Key Vault when Foundry/app secrets appear (slot reserved; not needed with MI today).
