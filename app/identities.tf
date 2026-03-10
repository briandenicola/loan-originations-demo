resource "azurerm_user_assigned_identity" "app" {
  name                = "loan-origination-identity"
  resource_group_name = azurerm_resource_group.apps.name
  location            = azurerm_resource_group.apps.location
}
