# ============================================================================
# Phase 3B — OFF-HOST BACKUP PROTECTION (data-protection foundation ONLY)
#
# Scope boundary: this module provisions ONLY Azure Storage data-protection
# resources. NO AKS, ACR, Azure PostgreSQL, Redis, Service Bus, App Service,
# or any application resource is permitted here.
#
# Portfolio Mode decisions:
#   - StorageV2 / Standard / LRS  (cheapest; documented tradeoff in ADR-008)
#   - private container, public access disabled, TLS 1.2 minimum
#   - Shared Key authorization DISABLED → Microsoft Entra ID / RBAC only
#   - versioning + blob soft delete + container soft delete (14d)
#   - time-based immutability policy, UNLOCKED, 1-day retention (test only)
#   - lifecycle policy to bound storage growth (30d current / 90d versions)
#   - network firewall: default_action = Deny + operator IP allowlist
#
# Scope: this root owns BACKUP STORAGE ONLY. The application stack (ACR,
# PostgreSQL Flexible Server, Redis, Service Bus, Container Apps) lives in
# ../main.tf and must never be applied from here — a plan in this directory
# contains no application resources (ADR-008 "Implementation location").
# ============================================================================

resource "azurerm_resource_group" "backup" {
  name     = var.resource_group_name
  location = var.location
}

resource "azurerm_storage_account" "backup" {
  name                     = var.storage_account_name
  resource_group_name      = azurerm_resource_group.backup.name
  location                 = azurerm_resource_group.backup.location
  account_tier             = "Standard"
  account_kind             = "StorageV2"
  account_replication_type = "LRS" # Portfolio Mode; see ADR-008 (LRS vs GRS)

  # --- Hardening ---
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false # public blob access disabled
  shared_access_key_enabled       = false # Entra ID / RBAC only (ADR-009)
  https_traffic_only_enabled      = true

  # --- Network restriction (ADR-009) ---
  # The public endpoint exists, but NOTHING is allowed by default: only the
  # allowlisted operator egress IPs can reach the data plane. Confidentiality of
  # the blobs also relies on private containers + Entra-only auth + Shared Key off.
  # Enterprise reference: drop this and use a private endpoint with
  # public_network_access_enabled = false (see ADR-009 trade-off note).
  public_network_access_enabled = true

  network_rules {
    default_action = "Deny"
    ip_rules       = var.allowed_ip_rules
    bypass         = ["AzureServices"]
  }

  # hierarchical namespace NOT enabled (not required for pg_dump artifacts)

  # --- Blob data protection (Step 5) ---
  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = var.blob_soft_delete_days
    }

    container_delete_retention_policy {
      days = var.container_soft_delete_days
    }
  }

  # --- Lifecycle management ---
  # Ordering contract (ADR-008): WORM 1d < soft delete 14d < lifecycle 30d.
  # Lifecycle therefore always fires after the protection windows have expired.
  # Applies to every container: a daily backup stays "live" 30 days, and its
  # bytes survive as a non-current version until the 90-day version rule.
}

resource "azurerm_storage_management_policy" "backup" {
  storage_account_id = azurerm_storage_account.backup.id

  rule {
    name    = "backup-retention"
    enabled = true
    filters {
      blob_types = ["blockBlob"]
    }
    actions {
      base_blob {
        delete_after_days_since_modification_greater_than = var.retention_current_days
      }
      version {
        delete_after_days_since_creation = var.retention_noncurrent_days
      }
      snapshot {
        delete_after_days_since_creation_greater_than = var.retention_noncurrent_days
      }
    }
  }
}

# --- Private container (Step 4) ---
resource "azurerm_storage_container" "postgres_backups" {
  name                  = "postgres-backups"
  storage_account_name  = azurerm_storage_account.backup.name
  container_access_type = "private"
}

resource "azurerm_storage_container" "protection_tests" {
  name                  = "protection-tests"
  storage_account_name  = azurerm_storage_account.backup.name
  container_access_type = "private"
}

# --- Time-based immutability / WORM (Step 6) ---
# UNLOCKED on purpose: portfolio technical test only.
# A LOCKED policy can never be unlocked or shortened → would poison cleanup.
#
# NOTE: azurerm_storage_container_immutability_policy was REMOVED from this
# module. The azurerm 3.x resource hung indefinitely on create (twice; no ARM
# write was ever issued — verified in the activity log). The policy is
# provisioned out-of-band by scripts/backup/bootstrap-immutability-policy.sh
# and verified at bootstrap. Locked mode remains enterprise-only (ADR-009).

# --- RBAC (Step 9): current developer identity, minimum practical role ---
data "azurerm_client_config" "current" {}

resource "azurerm_role_assignment" "backup_operator" {
  scope                = azurerm_storage_account.backup.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}
