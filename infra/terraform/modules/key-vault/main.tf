data "azurerm_client_config" "current" {}

# RBAC-authorized Key Vault for external credentials (e.g. the Azure DevOps PAT)
# that cannot be replaced by Azure RBAC. Secret VALUES are set out-of-band
# (az keyvault secret set) and are never managed by Terraform.
resource "azurerm_key_vault" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  enable_rbac_authorization = true
  purge_protection_enabled  = false # disposable test environment

  tags = var.tags
}
