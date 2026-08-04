output "id" {
  description = "Container registry id."
  value       = azurerm_container_registry.this.id
}

output "login_server" {
  description = "Registry login server (e.g. myregistry.azurecr.io)."
  value       = azurerm_container_registry.this.login_server
}

output "name" {
  description = "Container registry name."
  value       = azurerm_container_registry.this.name
}
