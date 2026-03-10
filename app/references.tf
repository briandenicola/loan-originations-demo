data "azurerm_client_config" "current" {}

# ACR is in the same resource group as the CAE (core_rg)
data "azurerm_container_registry" "this" {
  name                = split(".", var.acr_login_server)[0]
  resource_group_name = var.cae_rg
}