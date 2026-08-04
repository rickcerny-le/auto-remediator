output "id" {
  description = "Container Apps Job id."
  value       = azurerm_container_app_job.this.id
}

output "name" {
  description = "Container Apps Job name."
  value       = azurerm_container_app_job.this.name
}
