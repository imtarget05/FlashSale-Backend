terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 3.117.0, < 4.0.0"
    }
  }

  # Phase 3C: remote ENCRYPTED backend + native state locking.
  # The local terraform.tfstate was migrated here (real state, not a stub), so
  # `terraform init -migrate-state` would ask to copy; answer "yes".
  # Locking: azurerm backend uses blob leases — a second concurrent apply fails
  # fast instead of corrupting state. Container must exist first (see
  # bootstrap-tfstate-backend.sh), otherwise init fails with "container not found".
  backend "azurerm" {
    resource_group_name  = "rg-flashsale-tfstate"
    storage_account_name = "stflashs3ctfbk01"
    container_name       = "tfstate"
    key                  = "backup-storage.terraform.tfstate"
    use_azuread_auth     = true
  }
}

# storage_use_azuread = true → storage DATA-PLANE ops (container/blob mgmt)
# authenticate with Microsoft Entra ID instead of Shared Key, so the module
# works end-to-end with shared_access_key_enabled = false.
provider "azurerm" {
  features {}
  storage_use_azuread = true
}
