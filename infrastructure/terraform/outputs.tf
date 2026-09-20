output "acr_login_server" {
  value = azurerm_container_registry.acr.login_server
}

output "api_fqdn" {
  value = azurerm_container_app.api.latest_revision_fqdn
}

output "postgres_fqdn" {
  value = azurerm_postgresql_flexible_server.db.fqdn
}

output "redis_hostname" {
  value = azurerm_redis_cache.reservation.hostname
}

output "servicebus_namespace_name" {
  value = azurerm_servicebus_namespace.orders.name
}

output "postgres_admin_password" {
  value     = coalesce(var.postgres_admin_password, random_password.pg.result)
  sensitive = true
}
