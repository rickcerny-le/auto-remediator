############################################
# Derived per-component configuration
############################################
locals {
  registry_server = module.container_registry.login_server

  # A component pulls from ACR only when a real image was supplied; the public
  # placeholder default needs no registry block. These booleans are known at
  # plan time so they can safely drive the modules' dynamic registry blocks.
  registry_enabled = {
    api         = var.api_image != null
    web         = var.web_image != null
    scheduler   = var.scheduler_image != null
    remediation = var.remediation_image != null
  }

  # Endpoints wired into the apps. With managed identity the app uses these as
  # service endpoints (via DefaultAzureCredential) rather than connection strings.
  # AZURE_CLIENT_ID pins DefaultAzureCredential to our user-assigned identity.
  storage_env = {
    "ConnectionStrings__tables" = module.storage.table_endpoint
    "ConnectionStrings__blobs"  = module.storage.blob_endpoint
  }

  servicebus_env = {
    "ConnectionStrings__servicebus" = module.service_bus.fully_qualified_namespace
  }

  identity_env = {
    "AZURE_CLIENT_ID" = module.identity.client_id
  }

  foundry_env = {
    "Agents__FoundryEndpoint"     = module.ai_foundry.endpoint
    "Agents__ModelDeploymentName" = module.ai_foundry.model_deployment_name
  }

  # Only components that read secrets (the ADO PAT) need the Key Vault URI.
  keyvault_env = {
    "KeyVaultUri" = module.key_vault.vault_uri
  }
}

############################################
# Foundation
############################################
module "resource_group" {
  source   = "../../modules/resource-group"
  name     = "rg-${local.name_prefix}"
  location = var.location
  tags     = local.common_tags
}

module "log_analytics" {
  source              = "../../modules/log-analytics"
  name                = "log-${local.name_prefix}"
  resource_group_name = module.resource_group.name
  location            = var.location
  tags                = local.common_tags
}

module "container_registry" {
  source              = "../../modules/container-registry"
  name                = "${local.name_compact}acr${local.suffix}"
  resource_group_name = module.resource_group.name
  location            = var.location
  tags                = local.common_tags
}

############################################
# Backing services
############################################
module "storage" {
  source              = "../../modules/storage"
  name                = "${local.name_compact}st${local.suffix}"
  resource_group_name = module.resource_group.name
  location            = var.location
  tags                = local.common_tags
}

module "service_bus" {
  source              = "../../modules/service-bus"
  name                = "sb-${local.name_prefix}-${local.suffix}"
  resource_group_name = module.resource_group.name
  location            = var.location
  tags                = local.common_tags
}

module "ai_foundry" {
  source              = "../../modules/ai-foundry"
  name                = "ai-${local.name_prefix}-${local.suffix}"
  resource_group_name = module.resource_group.name
  location            = var.location
  model_name          = var.foundry_model_name
  model_version       = var.foundry_model_version
  model_capacity      = var.foundry_model_capacity
  tags                = local.common_tags
}

module "key_vault" {
  source              = "../../modules/key-vault"
  name                = "kv-${local.name_prefix}-${local.suffix}"
  resource_group_name = module.resource_group.name
  location            = var.location
  tags                = local.common_tags
}

############################################
# Identity + RBAC (scoped to the resources above)
############################################
module "identity" {
  source              = "../../modules/identity"
  name                = "id-${local.name_prefix}"
  resource_group_name = module.resource_group.name
  location            = var.location
  tags                = local.common_tags

  storage_account_id      = module.storage.id
  servicebus_namespace_id = module.service_bus.id
  container_registry_id   = module.container_registry.id
  ai_services_id          = module.ai_foundry.id
  key_vault_id            = module.key_vault.id
}

############################################
# Container Apps Environment
############################################
module "container_apps_environment" {
  source                     = "../../modules/container-apps-environment"
  name                       = "cae-${local.name_prefix}"
  resource_group_name        = module.resource_group.name
  location                   = var.location
  log_analytics_workspace_id = module.log_analytics.id
  tags                       = local.common_tags
}

############################################
# Apps: api + web (external ingress, scale-to-zero)
############################################
module "api" {
  source                       = "../../modules/container-app"
  name                         = "ca-${local.name_prefix}-api"
  resource_group_name          = module.resource_group.name
  container_app_environment_id = module.container_apps_environment.id
  user_assigned_identity_id    = module.identity.id
  image                        = local.images.api
  registry_enabled             = local.registry_enabled.api
  registry_server              = local.registry_server
  ingress_enabled              = true
  external_ingress             = true
  target_port                  = 8080
  env_vars                     = merge(local.storage_env, local.servicebus_env, local.identity_env, local.keyvault_env)
  tags                         = local.common_tags

  depends_on = [module.identity]
}

module "web" {
  source                       = "../../modules/container-app"
  name                         = "ca-${local.name_prefix}-web"
  resource_group_name          = module.resource_group.name
  container_app_environment_id = module.container_apps_environment.id
  user_assigned_identity_id    = module.identity.id
  image                        = local.images.web
  registry_enabled             = local.registry_enabled.web
  registry_server              = local.registry_server
  ingress_enabled              = true
  external_ingress             = true
  target_port                  = 8080
  env_vars = merge(
    local.identity_env,
    {
      # .NET service discovery target for the "api" client used by the Web app.
      "services__api__https__0" = "https://${module.api.fqdn}"
    },
  )
  tags = local.common_tags

  depends_on = [module.identity]
}

############################################
# Jobs: scheduler (cron) + remediation (event/KEDA)
############################################
module "scheduler_job" {
  source                       = "../../modules/container-apps-job"
  name                         = "caj-${local.name_prefix}-scheduler"
  resource_group_name          = module.resource_group.name
  location                     = var.location
  container_app_environment_id = module.container_apps_environment.id
  user_assigned_identity_id    = module.identity.id
  image                        = local.images.scheduler
  registry_enabled             = local.registry_enabled.scheduler
  registry_server              = local.registry_server

  trigger_type    = "Schedule"
  cron_expression = var.scheduler_cron_expression

  env_vars = merge(
    local.storage_env,
    local.servicebus_env,
    local.identity_env,
    local.keyvault_env,
    { "Scheduler__DevLoopEnabled" = "false" },
  )
  tags = local.common_tags

  depends_on = [module.identity]
}

module "remediation_job" {
  source                       = "../../modules/container-apps-job"
  name                         = "caj-${local.name_prefix}-remediation"
  resource_group_name          = module.resource_group.name
  location                     = var.location
  container_app_environment_id = module.container_apps_environment.id
  user_assigned_identity_id    = module.identity.id
  image                        = local.images.remediation
  registry_enabled             = local.registry_enabled.remediation
  registry_server              = local.registry_server

  trigger_type   = "Event"
  min_executions = 0
  max_executions = var.remediation_max_executions

  event_scale_rules = [
    {
      name             = "servicebus-queue-scaling"
      custom_rule_type = "azure-servicebus"
      metadata = {
        namespace    = module.service_bus.namespace_name
        queueName    = module.service_bus.queue_name
        messageCount = tostring(var.remediation_queue_scale_threshold)
      }
      # NOTE: the KEDA Service Bus scaler still needs auth (identity-based
      # preferred, or a listen-only connection-string secret). Tracked in the
      # README as a follow-up; app runtime auth itself uses managed identity.
      authentication = []
    },
  ]

  env_vars = merge(
    local.storage_env,
    local.servicebus_env,
    local.identity_env,
    local.foundry_env,
    local.keyvault_env,
  )
  tags = local.common_tags

  depends_on = [module.identity]
}
