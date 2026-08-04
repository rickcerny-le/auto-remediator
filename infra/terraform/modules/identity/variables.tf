variable "name" {
  type        = string
  description = "User-assigned managed identity name."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to create the identity in."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "tags" {
  type        = map(string)
  description = "Tags to apply."
  default     = {}
}

# Target resource ids to scope RBAC role assignments against. Any left null
# skips that assignment, keeping the module reusable.
variable "storage_account_id" {
  type        = string
  description = "Storage account id for Table/Blob data role assignments."
  default     = null
}

variable "servicebus_namespace_id" {
  type        = string
  description = "Service Bus namespace id for send/receive role assignments."
  default     = null
}

variable "container_registry_id" {
  type        = string
  description = "Container registry id for the AcrPull role assignment."
  default     = null
}

variable "ai_services_id" {
  type        = string
  description = "AI Services account id for the Cognitive Services OpenAI User role assignment."
  default     = null
}

variable "key_vault_id" {
  type        = string
  description = "Key Vault id for the Key Vault Secrets User role assignment."
  default     = null
}
