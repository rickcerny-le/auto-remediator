variable "name" {
  type        = string
  description = "Container registry name (globally unique, alphanumeric)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to create the registry in."
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
