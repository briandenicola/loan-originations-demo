output "APP_NAME" {
    value = local.resource_name
    sensitive = false
}

output "APP_RESOURCE_GROUP" {
    value = azurerm_resource_group.this.name
    sensitive = false
}

output "OPENAI_ENDPOINT" {
    value = data.azurerm_cognitive_account.ai_foundry.endpoint
    sensitive = false
}

output "FOUNDRY_ENDPOINT" {
    value = module.project_classic.PROJECT_ENDPOINT
    sensitive = false
}
output "FOUNDRY_NEXTGEN_ENDPOINT" {
    value = module.project_workflow.PROJECT_ENDPOINT
    sensitive = false
}

# ── Outputs for app/ deployment module ────────────────────────
output "ACR_NAME" {
    value     = azurerm_container_registry.this.name
    sensitive = false
}

output "ACR_LOGIN_SERVER" {
    value     = azurerm_container_registry.this.login_server
    sensitive = false
}

output "CAE_ID" {
    value     = azurerm_container_app_environment.this.id
    sensitive = false
}

output "CAE_RG" {
    value     = azurerm_resource_group.core.name
    sensitive = false
}

output "CLASSIC_APPINSIGHTS_CONNECTION_STRING" {
    value     = module.project_classic.APPINSIGHTS_CONNECTION_STRING
    sensitive = true
}

output "WORKFLOW_APPINSIGHTS_CONNECTION_STRING" {
    value     = module.project_workflow.APPINSIGHTS_CONNECTION_STRING
    sensitive = true
}

output "AI_SERVICES_ID" {
    value     = azapi_resource.ai_foundry.id
    sensitive = false
}