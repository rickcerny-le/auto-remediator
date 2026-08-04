output "id" {
  description = "Storage account id (RBAC assignment scope)."
  value       = azurerm_storage_account.this.id
}

output "name" {
  description = "Storage account name."
  value       = azurerm_storage_account.this.name
}

output "table_endpoint" {
  description = "Primary Table service endpoint (for TableServiceClient + DefaultAzureCredential)."
  value       = azurerm_storage_account.this.primary_table_endpoint
}

output "blob_endpoint" {
  description = "Primary Blob service endpoint (for BlobServiceClient + DefaultAzureCredential)."
  value       = azurerm_storage_account.this.primary_blob_endpoint
}

output "table_name" {
  description = "Created table name."
  value       = azurerm_storage_table.this.name
}

output "blob_container_name" {
  description = "Created blob container name."
  value       = azurerm_storage_container.this.name
}
