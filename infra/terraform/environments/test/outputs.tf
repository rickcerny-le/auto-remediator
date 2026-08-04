output "resource_group_name" {
  description = "Resource group holding the test environment."
  value       = module.resource_group.name
}

output "acr_login_server" {
  description = "Container registry login server — push images here as <server>/autoremediator/<component>:<tag>."
  value       = module.container_registry.login_server
}

output "api_fqdn" {
  description = "Public FQDN of the API app."
  value       = module.api.fqdn
}

output "web_fqdn" {
  description = "Public FQDN of the Web app."
  value       = module.web.fqdn
}

output "storage_table_endpoint" {
  description = "Storage Table service endpoint."
  value       = module.storage.table_endpoint
}

output "storage_blob_endpoint" {
  description = "Storage Blob service endpoint."
  value       = module.storage.blob_endpoint
}

output "servicebus_namespace" {
  description = "Service Bus namespace FQDN."
  value       = module.service_bus.fully_qualified_namespace
}

output "foundry_endpoint" {
  description = "AI Foundry (AI Services) endpoint — Agents:FoundryEndpoint."
  value       = module.ai_foundry.endpoint
}

output "foundry_model_deployment" {
  description = "Deployed model name — Agents:ModelDeploymentName."
  value       = module.ai_foundry.model_deployment_name
}

output "managed_identity_client_id" {
  description = "Client id of the shared user-assigned managed identity (AZURE_CLIENT_ID)."
  value       = module.identity.client_id
}

output "key_vault_uri" {
  description = "Key Vault URI (KeyVaultUri). Set the AzureDevOps--Pat secret here out-of-band."
  value       = module.key_vault.vault_uri
}

output "key_vault_name" {
  description = "Key Vault name (for az keyvault secret set)."
  value       = module.key_vault.name
}
