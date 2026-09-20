output "acr_login_server" {
  value = azurerm_container_registry.acr.login_server
}

output "acr_name" {
  value = azurerm_container_registry.acr.name
}

output "github_client_id" {
  value       = azurerm_user_assigned_identity.github_release.client_id
  description = "Managed identity client id for azure/login OIDC (non-secret)."
}

output "tenant_id" {
  value = data.azurerm_client_config.current.tenant_id
}

data "azurerm_client_config" "current" {}
