variable "name" {
  type        = string
  description = "Container Apps Job name."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group."
}

variable "location" {
  type        = string
  description = "Azure region."
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

variable "secrets" {
  type        = map(string)
  description = "Optional job secrets (name => value), e.g. a scaler connection string. Empty keeps the job secret-free."
  default     = {}
  sensitive   = true
}

variable "cpu" {
  type        = number
  description = "vCPU per replica."
  default     = 0.25
}

variable "memory" {
  type        = string
  description = "Memory per replica."
  default     = "0.5Gi"
}

variable "replica_timeout_in_seconds" {
  type        = number
  description = "Max time a replica may run before being stopped."
  default     = 1800
}

variable "replica_retry_limit" {
  type        = number
  description = "Retries for a failed replica."
  default     = 1
}

variable "trigger_type" {
  type        = string
  description = "Job trigger type: Schedule or Event."

  validation {
    condition     = contains(["Schedule", "Event"], var.trigger_type)
    error_message = "trigger_type must be either \"Schedule\" or \"Event\"."
  }
}

# --- Schedule trigger ---------------------------------------------------------
variable "cron_expression" {
  type        = string
  description = "Cron expression (UTC) when trigger_type = Schedule."
  default     = "0 */6 * * *"
}

# --- Event trigger (KEDA) -----------------------------------------------------
variable "min_executions" {
  type        = number
  description = "Minimum concurrent executions when trigger_type = Event."
  default     = 0
}

variable "max_executions" {
  type        = number
  description = "Maximum concurrent executions when trigger_type = Event."
  default     = 10
}

variable "polling_interval_in_seconds" {
  type        = number
  description = "KEDA polling interval."
  default     = 30
}

variable "event_scale_rules" {
  description = "KEDA scale rules for an Event-triggered job."
  type = list(object({
    name             = string
    custom_rule_type = string
    metadata         = map(string)
    authentication = optional(list(object({
      secret_name       = string
      trigger_parameter = string
    })), [])
  }))
  default = []
}

variable "tags" {
  type        = map(string)
  description = "Tags to apply."
  default     = {}
}
