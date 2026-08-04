resource "azurerm_container_registry" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                 = "Basic"
  # Budget/testing: use RBAC (AcrPull via managed identity) rather than the admin user.
  admin_enabled = false
  tags          = var.tags
}
