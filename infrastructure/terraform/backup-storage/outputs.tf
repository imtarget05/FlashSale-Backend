output "resource_group_name" {
  value = azurerm_resource_group.backup.name
}

output "storage_account_name" {
  value = azurerm_storage_account.backup.name
}

output "backup_container_name" {
  value = azurerm_storage_container.postgres_backups.name
}

output "test_container_name" {
  value = azurerm_storage_container.protection_tests.name
}

output "network_default_action" {
  value       = azurerm_storage_account.backup.network_rules[0].default_action
  description = "Storage firewall default action (must be Deny; ADR-009)."
}

output "network_allowed_ip_rules" {
  value       = azurerm_storage_account.backup.network_rules[0].ip_rules
  description = "Operator egress IPs allowlisted on the data plane (sensitive to IP changes)."
}
