variable "name" {
  type        = string
  description = "Container Apps Environment name."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to create the environment in."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "log_analytics_workspace_id" {
  type        = string
  description = "Log Analytics workspace id for environment logs."
}

variable "tags" {
  type        = map(string)
  description = "Tags to apply."
  default     = {}
}
