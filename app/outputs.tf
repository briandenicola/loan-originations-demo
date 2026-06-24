output "WORKFLOW_URL" {
  value = "https://${azurerm_container_app.workflow.ingress[0].fqdn}"
}

output "MANAGED_IDENTITY_CLIENT_ID" {
  value = azurerm_user_assigned_identity.app.client_id
}

output "OUTPUT_STORAGE_ACCOUNT" {
  value = azurerm_storage_account.outputs.name
}

output "OUTPUT_CONTAINER" {
  value = azurerm_storage_container.outputs.name
}
