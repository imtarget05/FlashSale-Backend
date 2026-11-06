variable "location" {
  description = "Azure region (canonical name)."
  type        = string
  default     = "southeastasia"
}

variable "resource_group_name" {
  description = "Resource group for the flash-sale stack."
  type        = string
  default     = "rg-flashsale-dev"
}

variable "postgres_admin_password" {
  description = "Explicit PostgreSQL admin password. Empty -> a random password is generated."
  type        = string
  default     = ""
  sensitive   = true
}

variable "image_tag" {
  description = "Container image tag deployed by the CD pipeline."
  type        = string
  default     = "latest"
}

variable "enable_postgres_ha" {
  description = "Enable Zone-Redundant High Availability for PostgreSQL Flexible Server (production grade)."
  type        = bool
  default     = false
}

variable "postgres_sku_name" {
  description = "SKU for PostgreSQL Flexible Server. Dev uses B_Standard_B1ms; HA/Prod uses GP_Standard_D2s_v3."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "redis_sku" {
  description = "SKU tier for Azure Cache for Redis: Basic (dev), Standard (replicated HA), Premium (clustered/multi-AZ)."
  type        = string
  default     = "Basic"
}

variable "oidc_issuer_uri" {
  description = "OIDC issuer URI cho profile production (bắt buộc khi bật profile production)."
  type        = string
  default     = ""
}

variable "oidc_audience" {
  description = "OIDC audience cho profile production (bắt buộc khi bật profile production)."
  type        = string
  default     = ""
}
