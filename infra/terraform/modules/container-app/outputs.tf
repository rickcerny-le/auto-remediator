output "id" {
  description = "Container app id."
  value       = azurerm_container_app.this.id
}

output "name" {
  description = "Container app name."
  value       = azurerm_container_app.this.name
}

output "fqdn" {
  description = "Ingress FQDN (null when ingress is disabled)."
  value       = try(azurerm_container_app.this.ingress[0].fqdn, null)
}
