variable "region" {
  description = "Region to deploy resources to"
  default     =  "eastus2"
}

variable "tags" {
  description = "Tags to apply to Resource Group"
}

variable "app_service_principal_object_id" {
  description = "Object ID of the App Service Principal in Azure AD"
}