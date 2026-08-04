resource "azurerm_servicebus_namespace" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                 = "Basic" # cheapest; supports queues (topics/sessions need Standard+)
  tags                = var.tags
}

resource "azurerm_servicebus_queue" "remediation_runs" {
  name         = var.queue_name
  namespace_id = azurerm_servicebus_namespace.this.id

  # Basic-tier compatible settings.
  max_delivery_count   = 10
  lock_duration        = "PT1M"
  default_message_ttl  = "P14D"
}
