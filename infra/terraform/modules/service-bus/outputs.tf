output "id" {
  description = "Service Bus namespace id (RBAC assignment scope)."
  value       = azurerm_servicebus_namespace.this.id
}

output "namespace_name" {
  description = "Service Bus namespace name."
  value       = azurerm_servicebus_namespace.this.name
}

output "fully_qualified_namespace" {
  description = "Namespace FQDN (for ServiceBusClient + DefaultAzureCredential), e.g. ns.servicebus.windows.net."
  value       = "${azurerm_servicebus_namespace.this.name}.servicebus.windows.net"
}

output "queue_name" {
  description = "Remediation queue name."
  value       = azurerm_servicebus_queue.remediation_runs.name
}
