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
