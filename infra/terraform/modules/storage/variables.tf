variable "name" {
  type        = string
  description = "Storage account name (globally unique, 3-24 lowercase alphanumeric)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to create the account in."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "table_name" {
  type        = string
  description = "Name of the table to create."
  default     = "repositories"
}

variable "blob_container_name" {
  type        = string
  description = "Name of the blob container to create."
  default     = "artifacts"
}

variable "tags" {
  type        = map(string)
  description = "Tags to apply."
  default     = {}
}
