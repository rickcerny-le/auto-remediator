# azure-test-environment Specification

## Purpose

Defines the budget-oriented Azure test environment for AutoRemediator: a scale-to-zero Container Apps Environment hosting the API and Web as Container Apps and the scheduler and remediation components as Container Apps Jobs, backed by lowest-tier registry, storage, and Service Bus services plus Azure AI Foundry model access, all accessed through a single user-assigned managed identity with RBAC rather than stored secrets, and deployable before application images exist.

## Requirements

### Requirement: Container Apps Environment on budget compute
The test environment SHALL provision an Azure Container Apps Environment using the Consumption workload profile (scale-to-zero capable), backed by a Log Analytics workspace for logs. No dedicated workload profiles or VNet integration SHALL be provisioned in this environment.

#### Scenario: ACA environment is Consumption-based
- **WHEN** the container-apps-environment module is applied
- **THEN** it creates a Container Apps Environment associated with a Log Analytics workspace, using Consumption (no dedicated workload profile, no custom VNet)

### Requirement: API and Web deployed as Container Apps
The `api` and `web` components SHALL each be deployed as a Container App with external ingress, able to scale to zero, using the user-assigned managed identity for registry pulls, and configured via environment variables (no inline secrets).

#### Scenario: API and Web run as ingress-enabled Container Apps
- **WHEN** the container-app module is instantiated for `api` and for `web`
- **THEN** each is created with external ingress, a configurable min-replica count that permits scale-to-zero, the shared managed identity attached, and its image pulled from the registry using that identity

### Requirement: Scheduler and remediation deployed as Container Apps Jobs
The `scheduler` component SHALL be deployed as a Container Apps Job with a **scheduled (cron)** trigger, and the `remediation` component SHALL be deployed as a Container Apps Job with an **event** trigger that scales on Service Bus `remediation-runs` queue depth (KEDA). Both SHALL use the shared managed identity.

#### Scenario: Scheduler is a cron job
- **WHEN** the container-apps-job module is instantiated for `scheduler`
- **THEN** it is created with a scheduled trigger driven by a configurable cron expression, executing to completion per run

#### Scenario: Remediation is an event-driven, queue-scaled job
- **WHEN** the container-apps-job module is instantiated for `remediation`
- **THEN** it is created with an event trigger and a Service Bus queue-length scale rule targeting `remediation-runs`, scaling executions with queue depth

### Requirement: Budget backing services
The environment SHALL provision an Azure Container Registry (Basic), a Storage account (Standard LRS) exposing a Table service and a Blob container, and a Service Bus namespace (Basic SKU) containing the `remediation-runs` queue. SKUs SHALL be the lowest tier that satisfies the app's needs.

#### Scenario: Backing services use lowest viable SKUs
- **WHEN** the storage, service-bus, and container-registry modules are applied
- **THEN** the registry is Basic, storage is Standard LRS with a table and a blob container, and the Service Bus namespace is Basic with the `remediation-runs` queue

### Requirement: Azure AI Foundry model access
The environment SHALL provision Azure AI Foundry access via an AI Services account with a `gpt-4o-mini` model deployment, and SHALL expose the account endpoint and deployment name so they can be supplied to the app as `Agents:FoundryEndpoint` and `Agents:ModelDeploymentName`.

#### Scenario: A gpt-4o-mini deployment is available
- **WHEN** the ai-foundry module is applied
- **THEN** it creates an AI Services account with a `gpt-4o-mini` deployment and outputs the endpoint and deployment name for the app configuration

### Requirement: Managed Identity and RBAC access model
Access to Storage, Service Bus, the container registry, and AI Foundry SHALL be granted to a single user-assigned managed identity via Azure RBAC role assignments; no connection strings or account keys SHALL be stored as secrets. At minimum the identity SHALL receive Storage Table and Blob data roles, Service Bus send and receive roles, AcrPull, and the Cognitive Services OpenAI user role.

#### Scenario: Apps authenticate via managed identity, not secrets
- **WHEN** the identity module and role assignments are applied
- **THEN** the user-assigned managed identity holds the Storage (table + blob) data roles, Service Bus send/receive roles, AcrPull, and Cognitive Services OpenAI user role, and the Container Apps reference resources by endpoint with that identity rather than via stored connection strings

### Requirement: Resource endpoints wired into the apps
Each Container App and Job SHALL receive the endpoints it needs (Storage table/blob endpoints, Service Bus namespace, and — for the remediation job — the Foundry endpoint and model deployment name) as environment variables, so the running app resolves services without hard-coded connection strings.

#### Scenario: Apps receive endpoint configuration
- **WHEN** the apps and jobs are applied
- **THEN** the `api` and `web` apps receive the Storage and Service Bus endpoints, and the `remediation` job additionally receives `Agents:FoundryEndpoint` and `Agents:ModelDeploymentName`, all as environment variables

### Requirement: Deployable before app images exist
Container image references SHALL be variables with defaults that allow `terraform apply` to succeed before the application images are pushed to the registry, and real image references SHALL be supplied per component without code changes.

#### Scenario: Apply succeeds with placeholder images
- **WHEN** the environment is applied without app images having been pushed
- **THEN** each Container App/Job uses its configurable image variable (defaulting to a publicly pullable placeholder), and supplying the real registry image reference later requires only a variable change

### Requirement: Key Vault for external credentials
The test environment SHALL provision an Azure Key Vault (RBAC-authorized) to hold external credentials that cannot be replaced by Azure RBAC — specifically the Azure DevOps PAT — and SHALL grant the shared user-assigned managed identity the Key Vault Secrets User role. The secret value SHALL be populated out-of-band and SHALL NOT be stored in Terraform state or variables. This complements, and does not replace, the managed-identity/RBAC model used for Azure resource access.

#### Scenario: Key Vault is provisioned with identity access
- **WHEN** the test environment is applied
- **THEN** a Key Vault exists and the user-assigned managed identity holds the Key Vault Secrets User role on it

#### Scenario: PAT secret is not in Terraform
- **WHEN** the Terraform configuration and state are inspected
- **THEN** the Key Vault holds a named slot for the Azure DevOps PAT but its value is not present in the Terraform configuration or state

#### Scenario: Apps receive the Key Vault URI
- **WHEN** the apps and jobs are applied
- **THEN** they receive the Key Vault URI as an environment variable so the app can read the PAT as a configuration source via managed identity
