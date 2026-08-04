output "id" {
  description = "AI Services account id (RBAC assignment scope)."
  value       = azurerm_cognitive_account.this.id
}

output "endpoint" {
  description = "Account endpoint (Agents:FoundryEndpoint)."
  value       = azurerm_cognitive_account.this.endpoint
}

output "model_deployment_name" {
  description = "Deployed model name (Agents:ModelDeploymentName)."
  value       = azurerm_cognitive_deployment.model.name
}
