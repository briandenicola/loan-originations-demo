resource "azurerm_role_assignment" "ai_foundry_developer" {
  depends_on = [
    azurerm_resource_group.this
  ]
  scope                = azurerm_resource_group.this.id
  role_definition_name = "Azure AI Developer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "ai_foundry_project_manager" {
  depends_on = [
    azurerm_resource_group.this
  ]
  scope                = azurerm_resource_group.this.id
  role_definition_name = "Azure AI Project Manager"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "ai_foundry_project_developer" {
  depends_on = [
   module.project_1
  ]
  scope                = module.project_1.PROJECT_RESOURCE_GROUP_ID
  role_definition_name = "Azure AI Developer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "ai_foundry_project_project_manager" {
  depends_on = [
    module.project_1
  ]
  scope                = module.project_1.PROJECT_RESOURCE_GROUP_ID
  role_definition_name = "Azure AI Project Manager"
  principal_id         = data.azurerm_client_config.current.object_id
}

# Entra ID: Grant current user Cognitive Services OpenAI User on AI Services account
# This enables token-based auth (ManagedIdentity / AzureCliCredential) for the .NET agent
resource "azurerm_role_assignment" "cognitive_services_openai_user" {
  depends_on = [
    azapi_resource.ai_foundry
  ]
  scope                = azapi_resource.ai_foundry.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = data.azurerm_client_config.current.object_id
}

# Entra ID: Grant Cognitive Services OpenAI Contributor for model management
resource "azurerm_role_assignment" "cognitive_services_openai_contributor" {
  depends_on = [
    azapi_resource.ai_foundry
  ]
  scope                = azapi_resource.ai_foundry.id
  role_definition_name = "Cognitive Services OpenAI Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}