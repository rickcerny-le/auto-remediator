variable "name" {
  type        = string
  description = "Container app name."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group."
}

variable "container_app_environment_id" {
  type        = string
  description = "Container Apps Environment id."
}

variable "user_assigned_identity_id" {
  type        = string
  description = "User-assigned managed identity id (attached and used for ACR pull)."
}

variable "image" {
  type        = string
  description = "Container image reference."
}

variable "registry_enabled" {
  type        = bool
  description = "Whether to attach an ACR registry block (managed-identity pull). False for public images."
  default     = false
}

variable "registry_server" {
  type        = string
  description = "ACR login server used when registry_enabled is true."
  default     = null
}

variable "env_vars" {
  type        = map(string)
  description = "Plain environment variables (name => value)."
  default     = {}
}

variable "cpu" {
  type        = number
  description = "vCPU per replica."
  default     = 0.25
}

variable "memory" {
  type        = string
  description = "Memory per replica (must pair with cpu, e.g. 0.25 -> 0.5Gi)."
  default     = "0.5Gi"
}

variable "min_replicas" {
  type        = number
  description = "Minimum replicas (0 enables scale-to-zero)."
  default     = 0
}

variable "max_replicas" {
  type        = number
  description = "Maximum replicas."
  default     = 2
}

variable "ingress_enabled" {
  type        = bool
  description = "Whether to expose ingress."
  default     = true
}

variable "external_ingress" {
  type        = bool
  description = "If ingress is enabled, whether it is external (internet-facing)."
  default     = true
}

variable "target_port" {
  type        = number
  description = "Container listening port (ASP.NET Core containers default to 8080)."
  default     = 8080
}

variable "tags" {
  type        = map(string)
  description = "Tags to apply."
  default     = {}
}
