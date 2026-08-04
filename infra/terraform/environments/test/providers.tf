provider "azurerm" {
  subscription_id = var.subscription_id

  features {
    resource_group {
      # Allow teardown of the disposable test environment even if resources linger.
      prevent_deletion_if_contains_resources = false
    }
  }
}

provider "random" {}
