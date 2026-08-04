output "id" {
  description = "Resource id of the user-assigned managed identity."
  value       = azurerm_user_assigned_identity.this.id
}

output "principal_id" {
  description = "Principal (object) id of the managed identity."
  value       = azurerm_user_assigned_identity.this.principal_id
}

output "client_id" {
  description = "Client id of the managed identity (used by DefaultAzureCredential / AZURE_CLIENT_ID)."
  value       = azurerm_user_assigned_identity.this.client_id
}
