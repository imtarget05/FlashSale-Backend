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
