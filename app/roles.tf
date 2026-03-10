# Pull container images from ACR
resource "azurerm_role_assignment" "acr_pull" {
  scope                            = data.azurerm_container_registry.this.id
  role_definition_name             = "AcrPull"
  principal_id                     = azurerm_user_assigned_identity.app.principal_id
  skip_service_principal_aad_check = true
}

# Allow the container apps to call Foundry agents via Entra ID
resource "azurerm_role_assignment" "cognitive_services_user" {
  scope                            = var.ai_services_id
  role_definition_name             = "Cognitive Services User"
  principal_id                     = azurerm_user_assigned_identity.app.principal_id
  skip_service_principal_aad_check = true
}

resource "azurerm_role_assignment" "ai_developer" {
  scope                            = var.ai_services_id
  role_definition_name             = "Azure AI Developer"
  principal_id                     = azurerm_user_assigned_identity.app.principal_id
  skip_service_principal_aad_check = true
}
