# AutoRemediator — Infrastructure (Terraform)

Modular Terraform that deploys AutoRemediator to Azure on **budget SKUs** for a
**test** environment. The Azure footprint maps 1:1 to the app components: `api`
and `web` (Container Apps), `scheduler` (scheduled/cron Job) and `remediation`
(event-driven, Service Bus queue-scaled Job), plus Storage, Service Bus, an
AI Foundry model, a container registry, and a shared managed identity.

## Layout

```
infra/terraform/
  modules/                       # reusable, environment-agnostic
    resource-group/
    log-analytics/
    identity/                    # user-assigned MI + RBAC role assignments
    container-registry/          # ACR Basic
    storage/                     # Standard LRS: table + blob container
    service-bus/                 # Basic namespace + remediation-runs queue
    ai-foundry/                  # AI Services account + gpt-4o-mini deployment
    container-apps-environment/  # Consumption env + Log Analytics
    container-app/               # reusable app (api, web)
    container-apps-job/          # reusable job (scheduler cron, remediation event)
  environments/
    test/                        # composes the modules for the test env
```

Environment roots (`environments/<env>/`) declare providers, versions, variables,
and **only** `module` blocks. All resources live in `modules/`.

## Prerequisites

- Terraform >= 1.9
- Azure CLI, authenticated: `az login`
- A target subscription with rights to create the resources below. Supply it via
  `ARM_SUBSCRIPTION_ID` (or `TF_VAR_subscription_id`).

## Workflow

```bash
cd infra/terraform/environments/test

export ARM_SUBSCRIPTION_ID=<your-subscription-guid>

terraform init          # downloads providers + modules
terraform fmt -recursive
terraform validate
terraform plan           # requires az login + subscription
terraform apply          # creates the environment
# ...
terraform destroy        # test env is disposable
```

## Budget choices

| Resource | SKU / mode | Note |
| --- | --- | --- |
| Container Apps Environment | Consumption | scale-to-zero, no VNet |
| api / web | Container Apps, min replicas 0 | external ingress, cold-start on first hit |
| scheduler | Container Apps **Job**, Schedule trigger | cron (`scheduler_cron_expression`), runs once per fire |
| remediation | Container Apps **Job**, Event trigger | KEDA Service Bus queue scaling |
| Container Registry | Basic | RBAC pull (AcrPull), admin user disabled |
| Storage | Standard LRS | table + blob container |
| Service Bus | Basic | queue only (topics/sessions need Standard) |
| AI Foundry | AI Services account + `gpt-4o-mini` | per-token billing, near-zero idle |
| Log Analytics | PerGB2018, 30-day | required by the ACA environment |

## Container images

Image references default to a public placeholder
(`mcr.microsoft.com/azuredocs/containerapps-helloworld:latest`) so `terraform
apply` succeeds **before** the app images exist. Once you have built and pushed
the app images to the registry, supply them (and Terraform attaches the ACR
managed-identity pull automatically):

```hcl
# environments/test/terraform.tfvars
api_image         = "<acr-login-server>/autoremediator/api:latest"
web_image         = "<acr-login-server>/autoremediator/web:latest"
scheduler_image   = "<acr-login-server>/autoremediator/scheduler:latest"
remediation_image = "<acr-login-server>/autoremediator/remediation:latest"
```

Building and pushing images (and any CD pipeline) is out of scope for this IaC.

## Access model — Managed Identity + RBAC (no secrets)

A single user-assigned managed identity is attached to every app/job and used
for ACR pulls. It receives: Storage Table + Blob Data Contributor, Service Bus
Data Sender + Receiver, AcrPull, and Cognitive Services OpenAI User. Apps get
**endpoints** as environment variables (`ConnectionStrings__tables`/`__blobs` =
service endpoints, `ConnectionStrings__servicebus` = namespace FQDN,
`Agents__FoundryEndpoint` / `Agents__ModelDeploymentName`), plus `AZURE_CLIENT_ID`
so `DefaultAzureCredential` selects this identity. No connection strings or keys
are stored.

### Azure DevOps PAT (Key Vault secret — set out-of-band)

The apps read the Azure DevOps PAT from Key Vault (secret name `AzureDevOps--Pat`,
which the app sees as config key `AzureDevOps:Pat`). Terraform provisions the vault
and grants the managed identity **Key Vault Secrets User**, but never stores the PAT
value. After `terraform apply`, set it once:

```bash
az keyvault secret set \
  --vault-name "$(terraform output -raw key_vault_name)" \
  --name "AzureDevOps--Pat" \
  --value "<your-azure-devops-pat>"   # Code: Read & Write, Pull Request: contribute, Packaging: Read
```

> The PAT needs **Code (Read & Write)** and **Pull Request (contribute)** for the
> remediation worker to push the `autoremediator/dependency-updates` branch and open
> PRs (Slice 2), plus **Packaging (Read)** to resolve feed versions.

Also set `AzureDevOps:OrganizationUrl` (e.g. `https://dev.azure.com/Orion180`) as an app
setting (or a `AzureDevOps--OrganizationUrl` Key Vault secret). Locally, supply both via
user-secrets instead of Key Vault.

### Required follow-up (application code)

`AutoRemediator.Infrastructure` currently constructs `TableServiceClient`,
`BlobServiceClient`, and `ServiceBusClient` from **connection strings**. To
authenticate with the managed identity above, a follow-up app change must switch
these to **endpoint + `DefaultAzureCredential`** (and point the Agents client at
the Foundry endpoint). This Terraform provisions the target (identity, roles,
endpoint env vars); the deployed apps will not connect until that code change
lands.

### KEDA scaler auth (remediation job)

The remediation job's Service Bus queue-scaling rule is defined but its scaler
authentication is left empty. KEDA needs its own auth to read queue depth —
identity-based auth is preferred (once available via the provider), or a
listen-only connection-string secret can be supplied through the job module's
`secrets` + `event_scale_rules[].authentication`. This is a deliberate follow-up
so the app runtime stays secret-free.

## State

The **test** environment uses **local** state (`terraform.tfstate` in
`environments/test/`). It is not shared or locked — treat the test env as
reproducible and disposable.

To move to a remote backend for a shared/long-lived environment, add an
`azurerm` backend to the environment's `versions.tf`, e.g.:

```hcl
terraform {
  backend "azurerm" {
    resource_group_name  = "rg-tfstate"
    storage_account_name = "<tfstate-account>"
    container_name       = "tfstate"
    key                  = "autoremediator/<env>.tfstate"
  }
}
```

then `terraform init -migrate-state`. Provision that state storage account once
(a small bootstrap root or by hand) before migrating.
