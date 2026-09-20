# Phase 6 — Release infrastructure root (ACR + GitHub OIDC federation).
# Isolated from the app stack (../main.tf) and from backup-storage: Terraform
# owns this infrastructure per ADR-011's ownership matrix.
terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 3.117.0, < 4.0.0"
    }
  }

  # Remote encrypted state in the same backend as backup-storage, different key.
  backend "azurerm" {
    resource_group_name  = "rg-flashsale-tfstate"
    storage_account_name = "stflashs3ctfbk01"
    container_name       = "tfstate"
    key                  = "release.terraform.tfstate"
    use_azuread_auth     = true
  }
}

provider "azurerm" {
  features {}
}
