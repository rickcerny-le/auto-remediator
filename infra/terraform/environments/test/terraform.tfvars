# Test environment defaults. subscription_id is intentionally omitted here —
# supply it via `ARM_SUBSCRIPTION_ID` / `TF_VAR_subscription_id` or `-var`.

project     = "autrem"
environment = "test"
location    = "eastus2"

tags = {
  owner   = "platform"
  purpose = "auto-remediator-testing"
}

# Container images default to the public placeholder until real images are pushed.
# api_image         = "<acr-login-server>/autoremediator/api:latest"
# web_image         = "<acr-login-server>/autoremediator/web:latest"
# scheduler_image   = "<acr-login-server>/autoremediator/scheduler:latest"
# remediation_image = "<acr-login-server>/autoremediator/remediation:latest"

scheduler_cron_expression         = "0 */6 * * *"
remediation_queue_scale_threshold = 5

foundry_model_name     = "gpt-4o-mini"
foundry_model_capacity = 10
