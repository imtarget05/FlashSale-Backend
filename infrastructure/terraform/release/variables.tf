variable "location" {
  type    = string
  default = "southeastasia"
}

variable "resource_group_name" {
  type    = string
  default = "rg-flashsale-release"
}

variable "acr_name" {
  type        = string
  default     = "acrflashsalep6"
  description = "Registry name (globally unique, lowercase)."
}

variable "github_org" {
  type    = string
  default = "imtarget05"
}

variable "github_repo" {
  type    = string
  default = "FlashSale-Backend"
}

# Immutable numeric IDs GitHub embeds in the OIDC subject (Phase 6 finding:
# AADSTS700213 until these matched the presented assertion).
variable "github_owner_id" {
  type        = string
  default     = "163159731"
  description = "GitHub owner (user/org) numeric id."
}

variable "github_repo_id" {
  type        = string
  default     = "1378231058"
  description = "GitHub repository numeric id."
}
