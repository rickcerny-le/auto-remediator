output "id" {
  description = "Container Apps Environment id."
  value       = azurerm_container_app_environment.this.id
}

output "default_domain" {
  description = "Default domain for apps in this environment."
  value       = azurerm_container_app_environment.this.default_domain
}
