# ── Output artifact storage ───────────────────────────────────
# Durable, shared storage for the JSON run artifacts the workflow produces.
# The container app writes blobs here using its managed identity (Entra ID).
resource "azurerm_storage_account" "outputs" {
  name                            = local.storage_account_name
  resource_group_name             = azurerm_resource_group.apps.name
  location                        = azurerm_resource_group.apps.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false

  tags = {
    Application = var.tags
    Components  = "Workflow output artifacts"
  }
}

resource "azurerm_storage_container" "outputs" {
  name                  = local.output_container_name
  storage_account_id    = azurerm_storage_account.outputs.id
  container_access_type = "private"
}
