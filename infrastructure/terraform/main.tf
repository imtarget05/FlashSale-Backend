terraform {
  required_version = ">= 1.5"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}

provider "azurerm" {
  features {}
}

resource "azurerm_resource_group" "rg" {
  name     = var.resource_group_name
  location = var.location
}

resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
}

resource "azurerm_container_registry" "acr" {
  name                = "acrflashsale${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  sku                 = "Basic"
  admin_enabled       = true
}

resource "azurerm_log_analytics_workspace" "law" {
  name                = "law-flashsale"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "random_password" "pg" {
  length           = 24
  special          = true
  override_special = "_-"
}

resource "random_password" "rabbitmq" {
  length           = 24
  special          = true
  override_special = "_-"
}

resource "azurerm_postgresql_flexible_server" "db" {
  name                   = "psql-flashsale-${random_string.suffix.result}"
  resource_group_name    = azurerm_resource_group.rg.name
  location               = azurerm_resource_group.rg.location
  version                = "15"
  administrator_login    = "psqladmin"
  administrator_password = coalesce(var.postgres_admin_password, random_password.pg.result)
  storage_mb             = 32768
  sku_name               = var.postgres_sku_name

  lifecycle {
    ignore_changes = [zone]
  }
}

resource "azurerm_postgresql_flexible_server_database" "flashsaledb" {
  charset   = "UTF8"
  collation = "en_US.utf8"
  name      = "FlashSaleDb"
  server_id = azurerm_postgresql_flexible_server.db.id
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "allow_azure" {
  name             = "AllowAzureServices"
  server_id        = azurerm_postgresql_flexible_server.db.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_servicebus_namespace" "orders" {
  name                = "sb-flashsale-${random_string.suffix.result}"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "Standard"
}

resource "azurerm_servicebus_queue" "orders" {
  name               = "orders"
  namespace_id       = azurerm_servicebus_namespace.orders.id
  lock_duration      = "PT1M"
  max_delivery_count = 4
}

resource "azurerm_container_app_environment" "env" {
  name                       = "cae-flashsale"
  location                   = azurerm_resource_group.rg.location
  resource_group_name        = azurerm_resource_group.rg.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.law.id
}

resource "azurerm_container_app" "rabbitmq" {
  name                         = "rabbitmq"
  resource_group_name          = azurerm_resource_group.rg.name
  container_app_environment_id = azurerm_container_app_environment.env.id
  revision_mode                = "Single"

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "rabbitmq"
      image  = "rabbitmq:3.13-management-alpine"
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "RABBITMQ_DEFAULT_USER"
        value = "flashsale"
      }
      env {
        name        = "RABBITMQ_DEFAULT_PASS"
        secret_name = "rabbitmq-password"
      }
    }
  }

  secret {
    name  = "rabbitmq-password"
    value = coalesce(var.rabbitmq_password, random_password.rabbitmq.result)
  }

  ingress {
    external_enabled = false
    target_port      = 5672
    transport        = "tcp"

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }
}

resource "azurerm_container_app" "payment_api" {
  name                         = "payment-api"
  resource_group_name          = azurerm_resource_group.rg.name
  container_app_environment_id = azurerm_container_app_environment.env.id
  revision_mode                = "Single"

  template {
    min_replicas = 1
    max_replicas = 10

    container {
      name   = "payment-api"
      image  = "${azurerm_container_registry.acr.login_server}/payment-service:${var.image_tag}"
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "SPRING_PROFILES_ACTIVE"
        value = "production"
      }
      env {
        name  = "SERVER_PORT"
        value = "8080"
      }
      env {
        name  = "SPRING_DATASOURCE_URL"
        value = "jdbc:postgresql://${azurerm_postgresql_flexible_server.db.fqdn}:5432/FlashSaleDb"
      }
      env {
        name  = "SPRING_DATASOURCE_USERNAME"
        value = "psqladmin"
      }
      env {
        name        = "SPRING_DATASOURCE_PASSWORD"
        secret_name = "db-password"
      }
      env {
        name  = "SPRING_RABBITMQ_HOST"
        value = "rabbitmq"
      }
      env {
        name  = "SPRING_RABBITMQ_PORT"
        value = "5672"
      }
      env {
        name        = "SPRING_RABBITMQ_USERNAME"
        secret_name = "rabbitmq-username"
      }
      env {
        name        = "SPRING_RABBITMQ_PASSWORD"
        secret_name = "rabbitmq-password"
      }
      env {
        name        = "OIDC_ISSUER_URI"
        secret_name = "oidc-issuer"
      }
      env {
        name        = "OIDC_AUDIENCE"
        secret_name = "oidc-audience"
      }
      env {
        name  = "OIDC_AUTHORITIES_CLAIM"
        value = "roles"
      }
      env {
        name  = "MANAGEMENT_ENDPOINTS_WEB_EXPOSURE_INCLUDE"
        value = "health,info,metrics,prometheus"
      }

      liveness_probe {
        path             = "/healthz"
        port             = 8080
        transport        = "HTTP"
        initial_delay    = 5
        interval_seconds = 10
      }
      readiness_probe {
        path             = "/health/live"
        port             = 8080
        transport        = "HTTP"
        interval_seconds = 10
      }
    }
  }

  secret {
    name  = "db-password"
    value = coalesce(var.postgres_admin_password, random_password.pg.result)
  }
  secret {
    name  = "rabbitmq-username"
    value = "flashsale"
  }
  secret {
    name  = "rabbitmq-password"
    value = coalesce(var.rabbitmq_password, random_password.rabbitmq.result)
  }
  secret {
    name  = "acr-password"
    value = azurerm_container_registry.acr.admin_password
  }
  secret {
    name  = "oidc-issuer"
    value = var.oidc_issuer_uri
  }
  secret {
    name  = "oidc-audience"
    value = var.oidc_audience
  }

  registry {
    server               = azurerm_container_registry.acr.login_server
    username             = azurerm_container_registry.acr.admin_username
    password_secret_name = "acr-password"
  }
}

resource "azurerm_container_app" "payment_worker" {
  name                         = "payment-worker"
  resource_group_name          = azurerm_resource_group.rg.name
  container_app_environment_id = azurerm_container_app_environment.env.id
  revision_mode                = "Single"

  template {
    min_replicas = 0
    max_replicas = 3

    container {
      name   = "payment-worker"
      image  = "${azurerm_container_registry.acr.login_server}/payment-service:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "SPRING_PROFILES_ACTIVE"
        value = "production"
      }
      env {
        name  = "SERVER_PORT"
        value = "8080"
      }
      env {
        name  = "SPRING_DATASOURCE_URL"
        value = "jdbc:postgresql://${azurerm_postgresql_flexible_server.db.fqdn}:5432/FlashSaleDb"
      }
      env {
        name  = "SPRING_DATASOURCE_USERNAME"
        value = "psqladmin"
      }
      env {
        name        = "SPRING_DATASOURCE_PASSWORD"
        secret_name = "db-password"
      }
      env {
        name  = "SPRING_RABBITMQ_HOST"
        value = "rabbitmq"
      }
      env {
        name  = "SPRING_RABBITMQ_PORT"
        value = "5672"
      }
      env {
        name        = "SPRING_RABBITMQ_USERNAME"
        secret_name = "rabbitmq-username"
      }
      env {
        name        = "SPRING_RABBITMQ_PASSWORD"
        secret_name = "rabbitmq-password"
      }
      env {
        name        = "OIDC_ISSUER_URI"
        secret_name = "oidc-issuer"
      }
      env {
        name        = "OIDC_AUDIENCE"
        secret_name = "oidc-audience"
      }
    }
  }

  secret {
    name  = "db-password"
    value = coalesce(var.postgres_admin_password, random_password.pg.result)
  }
  secret {
    name  = "rabbitmq-username"
    value = "flashsale"
  }
  secret {
    name  = "rabbitmq-password"
    value = coalesce(var.rabbitmq_password, random_password.rabbitmq.result)
  }
  secret {
    name  = "acr-password"
    value = azurerm_container_registry.acr.admin_password
  }
  secret {
    name  = "oidc-issuer"
    value = var.oidc_issuer_uri
  }
  secret {
    name  = "oidc-audience"
    value = var.oidc_audience
  }

  registry {
    server               = azurerm_container_registry.acr.login_server
    username             = azurerm_container_registry.acr.admin_username
    password_secret_name = "acr-password"
  }
}
