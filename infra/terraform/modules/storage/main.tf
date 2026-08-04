resource "azurerm_storage_account" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location

  account_tier             = "Standard"
  account_replication_type = "LRS" # cheapest redundancy for testing
  account_kind             = "StorageV2"

  min_tls_version = "TLS1_2"

  tags = var.tags
}

resource "azurerm_storage_table" "this" {
  name                 = var.table_name
  storage_account_name = azurerm_storage_account.this.name
}

resource "azurerm_storage_container" "this" {
  name                  = var.blob_container_name
  storage_account_name  = azurerm_storage_account.this.name
  container_access_type = "private"
}
