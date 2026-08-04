output "id" {
  description = "Key Vault id (RBAC assignment scope)."
  value       = azurerm_key_vault.this.id
}

output "vault_uri" {
  description = "Key Vault URI (KeyVaultUri configuration value)."
  value       = azurerm_key_vault.this.vault_uri
}

output "name" {
  description = "Key Vault name."
  value       = azurerm_key_vault.this.name
}
