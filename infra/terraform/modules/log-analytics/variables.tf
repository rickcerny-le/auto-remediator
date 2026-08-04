variable "name" {
  type        = string
  description = "Log Analytics workspace name."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to create the workspace in."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "retention_in_days" {
  type        = number
  description = "Log retention in days (30 is the minimum billable floor)."
  default     = 30
}

variable "tags" {
  type        = map(string)
  description = "Tags to apply."
  default     = {}
}
