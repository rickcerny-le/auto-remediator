variable "subscription_id" {
  type        = string
  description = "Azure subscription id to deploy into. Prefer setting via ARM_SUBSCRIPTION_ID or TF_VAR_subscription_id rather than committing it."
}

variable "project" {
  type        = string
  description = "Short project prefix used to derive resource names."
  default     = "autrem"

  validation {
    condition     = can(regex("^[a-z0-9]{3,10}$", var.project))
    error_message = "project must be 3-10 lowercase alphanumeric characters (used in globally-unique names)."
  }
}

variable "environment" {
  type        = string
  description = "Environment name (used in names and tags)."
  default     = "test"
}

variable "location" {
  type        = string
  description = "Azure region for all resources."
  default     = "eastus2"
}

variable "tags" {
  type        = map(string)
  description = "Additional tags merged onto the common tag set."
  default     = {}
}

# --- Container images ---------------------------------------------------------
# Default to a public placeholder so `terraform apply` succeeds before app images
# are pushed. Override per component with the real ACR image once available:
#   <acr-login-server>/autoremediator/<component>:<tag>
variable "placeholder_image" {
  type        = string
  description = "Public image used until real app images are pushed to the registry."
  default     = "mcr.microsoft.com/azuredocs/containerapps-helloworld:latest"
}

variable "api_image" {
  type        = string
  description = "Container image for the API app. Defaults to the placeholder."
  default     = null
}

variable "web_image" {
  type        = string
  description = "Container image for the Web app. Defaults to the placeholder."
  default     = null
}

variable "scheduler_image" {
  type        = string
  description = "Container image for the scheduler job. Defaults to the placeholder."
  default     = null
}

variable "remediation_image" {
  type        = string
  description = "Container image for the remediation job. Defaults to the placeholder."
  default     = null
}

# --- Scheduling / scaling -----------------------------------------------------
variable "scheduler_cron_expression" {
  type        = string
  description = "Cron expression for the scheduler Container Apps Job (UTC)."
  default     = "0 */6 * * *"
}

variable "remediation_queue_scale_threshold" {
  type        = number
  description = "Service Bus queue message count per replica that triggers the remediation job to scale."
  default     = 5
}

variable "remediation_max_executions" {
  type        = number
  description = "Maximum parallel executions for the event-driven remediation job."
  default     = 10
}

# --- AI Foundry model ---------------------------------------------------------
variable "foundry_model_name" {
  type        = string
  description = "Model to deploy on the AI Services account."
  default     = "gpt-4o-mini"
}

variable "foundry_model_version" {
  type        = string
  description = "Model version. Leave null to let Azure pick the default for the region."
  default     = null
}

variable "foundry_model_capacity" {
  type        = number
  description = "Deployment capacity (thousands of tokens per minute) — keep small for testing."
  default     = 10
}
