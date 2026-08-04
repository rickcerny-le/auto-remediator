variable "name" {
  type        = string
  description = "AI Services account name (also used as the custom subdomain; globally unique)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to create the account in."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "model_name" {
  type        = string
  description = "Model to deploy."
  default     = "gpt-4o-mini"
}

variable "model_version" {
  type        = string
  description = "Model version. Null lets Azure pick the region default."
  default     = null
}

variable "model_sku_name" {
  type        = string
  description = "Deployment SKU (GlobalStandard is broadly available and token-billed)."
  default     = "GlobalStandard"
}

variable "model_capacity" {
  type        = number
  description = "Deployment capacity (thousands of tokens/min)."
  default     = 10
}

variable "tags" {
  type        = map(string)
  description = "Tags to apply."
  default     = {}
}
