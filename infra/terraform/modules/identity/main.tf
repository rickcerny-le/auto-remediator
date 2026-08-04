resource "azurerm_user_assigned_identity" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags
}

# --- Storage (Table + Blob data planes) --------------------------------------
resource "azurerm_role_assignment" "storage_table" {
  count                = var.storage_account_id == null ? 0 : 1
  scope                = var.storage_account_id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

resource "azurerm_role_assignment" "storage_blob" {
  count                = var.storage_account_id == null ? 0 : 1
  scope                = var.storage_account_id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

# --- Service Bus (send + receive) --------------------------------------------
resource "azurerm_role_assignment" "servicebus_sender" {
  count                = var.servicebus_namespace_id == null ? 0 : 1
  scope                = var.servicebus_namespace_id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

resource "azurerm_role_assignment" "servicebus_receiver" {
  count                = var.servicebus_namespace_id == null ? 0 : 1
  scope                = var.servicebus_namespace_id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

# --- Container Registry (image pull) -----------------------------------------
resource "azurerm_role_assignment" "acr_pull" {
  count                = var.container_registry_id == null ? 0 : 1
  scope                = var.container_registry_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

# --- AI Foundry / AI Services (model inference) ------------------------------
resource "azurerm_role_assignment" "openai_user" {
  count                = var.ai_services_id == null ? 0 : 1
  scope                = var.ai_services_id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

# --- Key Vault (read external secrets, e.g. the Azure DevOps PAT) -------------
resource "azurerm_role_assignment" "key_vault_secrets_user" {
  count                = var.key_vault_id == null ? 0 : 1
  scope                = var.key_vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}
