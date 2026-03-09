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
    value = module.project_1.PROJECT_ENDPOINT
    sensitive = false
}
output "FOUNDRY_NEXTGEN_ENDPOINT" {
    value = module.project_2.PROJECT_ENDPOINT
    sensitive = false
}