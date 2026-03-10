variable "region" {
  description = "Region to deploy resources to (must match infrastructure)"
  default     = "eastus2"
}

variable "tags" {
  description = "Application tag for resource groups"
  type        = string
}

variable "commit_version" {
  description = "Container image tag (short git SHA)"
  type        = string
}

variable "acr_login_server" {
  description = "ACR login server (e.g. myacr.azurecr.io)"
  type        = string
}

variable "cae_id" {
  description = "Container App Environment resource ID"
  type        = string
}

variable "foundry_endpoint_classic" {
  description = "Foundry project endpoint for the classic agent"
  type        = string
}

variable "foundry_endpoint_workflow" {
  description = "Foundry project endpoint for the workflow agent"
  type        = string
}

variable "appinsights_cs_classic" {
  description = "Application Insights connection string for classic"
  type        = string
  sensitive   = true
}

variable "appinsights_cs_workflow" {
  description = "Application Insights connection string for workflow"
  type        = string
  sensitive   = true
}

variable "ai_services_id" {
  description = "Resource ID of the AI Services (Foundry) account for RBAC"
  type        = string
}

variable "cae_rg" {
  description = "Resource group name containing the CAE and ACR"
  type        = string
}