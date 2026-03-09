
data "azurerm_client_config" "current" {}

locals {
  # User principals get all roles including Project Manager
  user_principals = {
    logged_in_user = {
      id             = data.azurerm_client_config.current.object_id
      principal_type = "User"
    }
    application_owner = {
      id             = var.application_owner_object_id
      principal_type = "User"
    }
  }

  # SPN principals cannot be assigned Project Manager role
  spn_principals = {
    app_spn = {
      id             = var.app_service_principal_object_id
      principal_type = "ServicePrincipal"
    }
  }

  all_principals = merge(local.user_principals, local.spn_principals)
}

resource "azurerm_role_assignment" "ai_foundry_developer" {
  for_each = local.all_principals
  depends_on = [
    azurerm_resource_group.this
  ]
  scope                = azurerm_resource_group.this.id
  role_definition_name = "Azure AI Developer"
  principal_id         = each.value.id
  principal_type       = each.value.principal_type
}

resource "azurerm_role_assignment" "ai_foundry_project_manager" {
  for_each = local.user_principals
  depends_on = [
    azurerm_resource_group.this
  ]
  scope                = azurerm_resource_group.this.id
  role_definition_name = "Azure AI Project Manager"
  principal_id         = each.value.id
  principal_type       = each.value.principal_type
}

resource "azurerm_role_assignment" "cognitive_services_openai_user" {
  for_each = local.all_principals
  depends_on = [
    azapi_resource.ai_foundry_project
  ]
  scope                = azapi_resource.ai_foundry_project.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = each.value.id
  principal_type       = each.value.principal_type
}

resource "azurerm_role_assignment" "cognitive_services_openai_contributor" {
  for_each = local.all_principals
  depends_on = [
    azapi_resource.ai_foundry_project
  ]
  scope                = azapi_resource.ai_foundry_project.id
  role_definition_name = "Cognitive Services OpenAI Contributor"
  principal_id         = each.value.id
  principal_type       = each.value.principal_type
}

# resource "azurerm_role_assignment" "cosmosdb_operator_ai_foundry_project" {
#   depends_on = [
#     resource.time_sleep.wait_project_identities
#   ]
#   scope                = azurerm_cosmosdb_account.this.id
#   role_definition_name = "Cosmos DB Operator"
#   principal_id         = azapi_resource.ai_foundry_project.output.identity.principalId
# }

# resource "azurerm_role_assignment" "storage_blob_data_contributor_ai_foundry_project" {
#   depends_on = [
#     resource.time_sleep.wait_project_identities
#   ]
#   scope                = azurerm_storage_account.this.id
#   role_definition_name = "Storage Blob Data Contributor"
#   principal_id         = azapi_resource.ai_foundry_project.output.identity.principalId
# }

# resource "azurerm_role_assignment" "search_index_data_contributor_ai_foundry_project" {
#   depends_on = [
#     resource.time_sleep.wait_project_identities
#   ]
#   scope                = azapi_resource.ai_search.id
#   role_definition_name = "Search Index Data Contributor"
#   principal_id         = azapi_resource.ai_foundry_project.output.identity.principalId
# }

# resource "azurerm_role_assignment" "search_service_contributor_ai_foundry_project" {
#   depends_on = [
#     resource.time_sleep.wait_project_identities
#   ]
#   scope                = azapi_resource.ai_search.id
#   role_definition_name = "Search Service Contributor"
#   principal_id         = azapi_resource.ai_foundry_project.output.identity.principalId
# }

# resource "azurerm_cosmosdb_sql_role_assignment" "cosmosdb_db_sql_role_aifp_user_thread_message_store" {
#   depends_on = [
#     azapi_resource.ai_foundry_project_capability_host
#   ]
#   resource_group_name = azurerm_cosmosdb_account.this.resource_group_name
#   account_name        = azurerm_cosmosdb_account.this.name
#   scope               = "${azurerm_cosmosdb_account.this.id}/dbs/enterprise_memory/colls/${local.project_id_guid}-thread-message-store"
#   role_definition_id  = "${azurerm_cosmosdb_account.this.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"
#   principal_id        = azapi_resource.ai_foundry_project.output.identity.principalId
# }

# resource "azurerm_cosmosdb_sql_role_assignment" "cosmosdb_db_sql_role_aifp_system_thread_name" {
#   depends_on = [
#     azurerm_cosmosdb_sql_role_assignment.cosmosdb_db_sql_role_aifp_user_thread_message_store
#   ]
#   resource_group_name = azurerm_cosmosdb_account.this.resource_group_name
#   account_name        = azurerm_cosmosdb_account.this.name
#   scope               = "${azurerm_cosmosdb_account.this.id}/dbs/enterprise_memory/colls/${local.project_id_guid}-system-thread-message-store"
#   role_definition_id  = "${azurerm_cosmosdb_account.this.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"
#   principal_id        = azapi_resource.ai_foundry_project.output.identity.principalId
# }

# resource "azurerm_cosmosdb_sql_role_assignment" "cosmosdb_db_sql_role_aifp_entity_store_name" {
#   depends_on = [
#     azurerm_cosmosdb_sql_role_assignment.cosmosdb_db_sql_role_aifp_system_thread_name
#   ]
#   resource_group_name = azurerm_cosmosdb_account.this.resource_group_name
#   account_name        = azurerm_cosmosdb_account.this.name
#   scope               = "${azurerm_cosmosdb_account.this.id}/dbs/enterprise_memory/colls/${local.project_id_guid}-agent-entity-store"
#   role_definition_id  = "${azurerm_cosmosdb_account.this.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"
#   principal_id        = azapi_resource.ai_foundry_project.output.identity.principalId
# }

# resource "azurerm_role_assignment" "storage_blob_data_owner_ai_foundry_project" {
#   depends_on = [
#     azapi_resource.ai_foundry_project_capability_host
#   ]
#   scope                = azurerm_storage_account.this.id
#   role_definition_name = "Storage Blob Data Owner"
#   principal_id         = azapi_resource.ai_foundry_project.output.identity.principalId
#   condition_version    = "2.0"
#   condition            = <<-EOT
#   (
#     (
#       !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/read'})
#       AND !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/filter/action'})
#       AND !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/write'})
#     )
#     OR
#     (@Resource[Microsoft.Storage/storageAccounts/blobServices/containers:name] StringStartsWithIgnoreCase '${local.project_id_guid}'
#     AND @Resource[Microsoft.Storage/storageAccounts/blobServices/containers:name] StringLikeIgnoreCase '*-azureml-agent')
#   )
#   EOT
# }
