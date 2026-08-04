## ADDED Requirements

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
