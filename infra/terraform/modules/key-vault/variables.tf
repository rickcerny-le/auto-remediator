variable "name" {
  type        = string
  description = "Key Vault name (globally unique, 3-24 chars)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to create the vault in."
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
