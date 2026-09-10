variable "name" {
  type        = string
  description = "Service Bus namespace name (globally unique)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to create the namespace in."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "queue_name" {
  type        = string
  description = "Queue carrying RemediationRunRequested messages."
  default     = "remediation-runs"
}

variable "review_commands_queue_name" {
  type        = string
  description = "Queue carrying ReviewCommandRequested messages."
  default     = "review-commands"
}

variable "tags" {
  type        = map(string)
  description = "Tags to apply."
  default     = {}
}
