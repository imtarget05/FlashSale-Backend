variable "location" {
  type        = string
  default     = "southeastasia"
  description = "Azure region for the backup data-protection foundation."
}

variable "resource_group_name" {
  type        = string
  default     = "rg-flashsale-data-protection"
  description = "Resource group owning ONLY backup infrastructure (no AKS)."
}

variable "storage_account_name" {
  type        = string
  default     = "stflashsalebackup"
  description = "Globally-unique storage account name (lowercase a-z0-9, 3-24 chars)."
}

variable "blob_soft_delete_days" {
  type        = number
  default     = 14
  description = "Portfolio blob soft-delete retention (14d > 7d RPO window; cheap for KB-size dumps)."
}

variable "container_soft_delete_days" {
  type        = number
  default     = 14
  description = "Portfolio container soft-delete retention; matches blob retention."
}

variable "immutability_retention_days" {
  type        = number
  default     = 1
  description = "Shortest test interval. UNLOCKED by design; locked = enterprise only."
}

# --- Lifecycle (ADR-008): WORM 1d < soft delete 14d < lifecycle 30d ---
variable "retention_current_days" {
  type        = number
  default     = 30
  description = "Delete current backup blobs after N days (≈30 daily recovery points)."
}

variable "retention_noncurrent_days" {
  type        = number
  default     = 90
  description = "Delete non-current versions / snapshots after N days (late-discovery protection)."
}

# --- Network restriction (ADR-009) ---
variable "allowed_ip_rules" {
  type        = list(string)
  description = "Operator egress IPs allowed to reach the storage data plane (default_action = Deny)."
  default     = []

  validation {
    condition     = length(var.allowed_ip_rules) > 0
    error_message = "Provide at least one allowlisted CIDR (e.g. [\"203.0.113.7/32\"]). With default_action = Deny an empty allowlist would block the backup job itself. Recover a wrong value with: az storage account update --default-action Allow."
  }
}
