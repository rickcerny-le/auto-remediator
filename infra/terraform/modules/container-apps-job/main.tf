resource "azurerm_container_app_job" "this" {
  name                         = var.name
  location                     = var.location
  resource_group_name          = var.resource_group_name
  container_app_environment_id = var.container_app_environment_id
  replica_timeout_in_seconds   = var.replica_timeout_in_seconds
  replica_retry_limit          = var.replica_retry_limit
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.user_assigned_identity_id]
  }

  dynamic "registry" {
    for_each = var.registry_enabled ? [1] : []
    content {
      server   = var.registry_server
      identity = var.user_assigned_identity_id
    }
  }

  # Optional secrets (e.g. a listen-only connection string for the KEDA scaler).
  dynamic "secret" {
    for_each = var.secrets
    content {
      name  = secret.key
      value = secret.value
    }
  }

  # --- Scheduled (cron) trigger: scheduler runs once per fire, then exits ------
  dynamic "schedule_trigger_config" {
    for_each = var.trigger_type == "Schedule" ? [1] : []
    content {
      cron_expression          = var.cron_expression
      parallelism              = 1
      replica_completion_count = 1
    }
  }

  # --- Event trigger: remediation scales on Service Bus queue depth (KEDA) -----
  dynamic "event_trigger_config" {
    for_each = var.trigger_type == "Event" ? [1] : []
    content {
      parallelism              = 1
      replica_completion_count = 1

      scale {
        min_executions              = var.min_executions
        max_executions              = var.max_executions
        polling_interval_in_seconds = var.polling_interval_in_seconds

        dynamic "rules" {
          for_each = var.event_scale_rules
          content {
            name             = rules.value.name
            custom_rule_type = rules.value.custom_rule_type
            metadata         = rules.value.metadata

            dynamic "authentication" {
              for_each = rules.value.authentication
              content {
                secret_name       = authentication.value.secret_name
                trigger_parameter = authentication.value.trigger_parameter
              }
            }
          }
        }
      }
    }
  }

  template {
    container {
      name   = var.name
      image  = var.image
      cpu    = var.cpu
      memory = var.memory

      dynamic "env" {
        for_each = var.env_vars
        content {
          name  = env.key
          value = env.value
        }
      }
    }
  }
}
