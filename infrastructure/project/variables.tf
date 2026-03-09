variable "foundry_project" {
  type = object({
    name          = string
    resource_name = string
    location      = string
    ai_foundry    = object({
      name        = string
      id          = string
    }) 
    tag           = string
    logs = object({
      workspace_id = string
    })
    models = list(object({
      name            = string
      version         = string
      format          = string
    }))
  })
}

variable "application_owner_object_id" {
  description = "The user that owners the environment"
}

variable "app_service_principal_object_id" {
  description = "The SPN that will deploy the agents and workflows"
}