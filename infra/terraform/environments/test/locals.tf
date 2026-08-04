# Deterministic uniqueness suffix for globally-unique resource names
# (storage account, container registry).
resource "random_string" "suffix" {
  length  = 6
  lower   = true
  upper   = false
  numeric = true
  special = false
}

locals {
  # Base name fragment, e.g. "autrem-test".
  name_prefix = "${var.project}-${var.environment}"

  # Compact, alphanumeric-only fragment for names that disallow hyphens,
  # e.g. "autremtest" + suffix for storage account / ACR.
  name_compact = "${var.project}${var.environment}"

  suffix = random_string.suffix.result

  common_tags = merge(
    {
      project    = var.project
      environment = var.environment
      managed-by = "terraform"
    },
    var.tags,
  )

  # Resolve each component's image, falling back to the placeholder.
  images = {
    api         = coalesce(var.api_image, var.placeholder_image)
    web         = coalesce(var.web_image, var.placeholder_image)
    scheduler   = coalesce(var.scheduler_image, var.placeholder_image)
    remediation = coalesce(var.remediation_image, var.placeholder_image)
  }
}
