# Azure AI Foundry access via an AI Services account. This is the lean, budget
# path (endpoint + model) rather than the full Foundry hub/project, which would
# also require Storage + Key Vault + App Insights.
resource "azurerm_cognitive_account" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location
  kind                = "AIServices"
  sku_name            = "S0"

  # Custom subdomain is required for AAD/token-based (managed identity) access.
  custom_subdomain_name         = var.name
  public_network_access_enabled = true
  local_auth_enabled            = false # force managed-identity/AAD auth, no keys

  tags = var.tags
}

resource "azurerm_cognitive_deployment" "model" {
  name                 = var.model_name
  cognitive_account_id = azurerm_cognitive_account.this.id

  model {
    format  = "OpenAI"
    name    = var.model_name
    version = var.model_version
  }

  sku {
    name     = var.model_sku_name
    capacity = var.model_capacity
  }
}
