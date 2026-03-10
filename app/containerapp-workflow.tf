# ── Workflow Agent Container App ──────────────────────────────
resource "azurerm_container_app" "workflow" {
  name                         = local.workflow_app_name
  container_app_environment_id = var.cae_id
  resource_group_name          = azurerm_resource_group.apps.name
  revision_mode                = "Single"

  identity {
    type = "UserAssigned"
    identity_ids = [
      azurerm_user_assigned_identity.app.id
    ]
  }

  ingress {
    allow_insecure_connections = false
    external_enabled           = true
    target_port                = 8080
    transport                  = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  registry {
    server   = var.acr_login_server
    identity = azurerm_user_assigned_identity.app.id
  }

  template {
    container {
      name   = "workflow"
      image  = local.workflow_image
      cpu    = 1
      memory = "2Gi"

      env {
        name  = "AzureOpenAI__Endpoint"
        value = var.foundry_endpoint_workflow
      }

      env {
        name  = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        value = var.appinsights_cs_workflow
      }

      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.app.client_id
      }
    }

    max_replicas = 3
    min_replicas = 1
  }
}
