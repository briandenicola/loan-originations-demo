locals {
  rg_name           = "loan-origination-apps_rg"
  classic_app_name  = "loan-origination-classic"
  workflow_app_name = "loan-origination-workflow"
  classic_image     = "${var.acr_login_server}/loan-origination-classic:${var.commit_version}"
  workflow_image    = "${var.acr_login_server}/loan-origination-workflow:${var.commit_version}"
}
