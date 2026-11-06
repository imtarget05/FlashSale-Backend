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
  # Remote state lives in an Azure Storage backend. Create it once, then:
  #   terraform init -backend-config="storage_account_name=<acct>" ...
  # backend "azurerm" {
  #   resource_group_name  = "rg-tfstate"
  #   storage_account_name = "<tfstate-account>"
  #   container_name       = "tfstate"
  #   key                  = "flashsale.terraform.tfstate"
  # }
}

provider "azurerm" {
  features {}
}

# ---------------------------------------------------------------
# Foundation
# ---------------------------------------------------------------
resource "azurerm_resource_group" "rg" {
  name     = var.resource_group_name
  location = var.location
}

resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
}

# ---------------------------------------------------------------
# Container Registry (images for api + worker)
# ---------------------------------------------------------------
resource "azurerm_container_registry" "acr" {
  name                = "acrflashsale${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  sku                 = "Basic"
  admin_enabled       = true
}

# ---------------------------------------------------------------
# Observability (Phase 10 landing zone)
# ---------------------------------------------------------------
resource "azurerm_log_analytics_workspace" "law" {
  name                = "law-flashsale"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

# ---------------------------------------------------------------
# PostgreSQL Flexible Server (source of truth)
# ---------------------------------------------------------------
resource "random_password" "pg" {
  length           = 24
  special          = true
  override_special = "-_"
}

resource "azurerm_postgresql_flexible_server" "db" {
  name                   = "psql-flashsale-${random_string.suffix.result}"
  resource_group_name    = azurerm_resource_group.rg.name
  location               = azurerm_resource_group.rg.location
  version                = "15"
  administrator_login    = "psqladmin"
  administrator_password = coalesce(var.postgres_admin_password, random_password.pg.result)
  storage_mb             = 32768
  sku_name               = var.enable_postgres_ha ? "GP_Standard_D2s_v3" : var.postgres_sku_name

  dynamic "high_availability" {
    for_each = var.enable_postgres_ha ? [1] : []
    content {
      mode = "ZoneRedundant"
    }
  }

  # Phase 4 lesson: reserve headroom above the app pool (80).
  # 200 max connections leaves room for migrations, operators and the worker.
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

# ---------------------------------------------------------------
# Redis (fast-fail reservation tier, ADR-003)
# ---------------------------------------------------------------
resource "azurerm_redis_cache" "reservation" {
  name                = "redis-flashsale-${random_string.suffix.result}"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  capacity            = var.redis_sku == "Premium" ? 1 : (var.redis_sku == "Standard" ? 1 : 0)
  family              = var.redis_sku == "Premium" ? "P" : "C"
  sku_name            = var.redis_sku

  redis_configuration {
    maxmemory_policy = "noeviction" # never silently drop reservation state
  }
}

# ---------------------------------------------------------------
# Azure Service Bus (async fulfillment, ADR-004)
# ---------------------------------------------------------------
resource "azurerm_servicebus_namespace" "orders" {
  name                = "sb-flashsale-${random_string.suffix.result}"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "Standard"
}

resource "azurerm_servicebus_queue" "orders" {
  name         = "orders"
  namespace_id = azurerm_servicebus_namespace.orders.id

  lock_duration                        = "PT1M"
  max_delivery_count                   = 4 # mirrors OrderProcessor.MaxAttempts
  dead_lettering_on_message_expiration = false
}

# ---------------------------------------------------------------
# Azure Container Apps Environment (Phase 7 compute)
# ---------------------------------------------------------------
resource "azurerm_container_app_environment" "env" {
  name                       = "cae-flashsale"
  location                   = azurerm_resource_group.rg.location
  resource_group_name        = azurerm_resource_group.rg.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.law.id
}

resource "azurerm_container_app" "payment_api" {
  name                         = "payment-api"
  resource_group_name          = azurerm_resource_group.rg.name
  container_app_environment_id = azurerm_container_app_environment.env.id
  revision_mode                = "Single"

  template {
    min_replicas = 1
    max_replicas = 3

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
        name  = "SPRING_DATA_REDIS_HOST"
        value = azurerm_redis_cache.reservation.hostname
      }
      env {
        name  = "SPRING_DATA_REDIS_PORT"
        value = "6380"
      }
      env {
        name  = "SPRING_DATA_REDIS_SSL_ENABLED"
        value = "true"
      }
      env {
        name        = "SPRING_DATA_REDIS_PASSWORD"
        secret_name = "redis-password"
      }
      env {
        name  = "SPRING_RABBITMQ_HOST"
        value = "${azurerm_servicebus_namespace.orders.name}.servicebus.windows.net"
      }
      env {
        name  = "SPRING_RABBITMQ_PORT"
        value = "5672"
      }
      env {
        name        = "SPRING_RABBITMQ_USERNAME"
        secret_name = "sb-username"
      }
      env {
        name        = "SPRING_RABBITMQ_PASSWORD"
        secret_name = "sb-password"
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
        initial_delay_seconds = 10
        interval_seconds = 10
      }
      readiness_probe {
        path             = "/health/live"
        port             = 8080
        transport        = "HTTP"
        initial_delay_seconds = 10
        interval_seconds = 10
      }
    }
  }

  secret {
    name  = "db-password"
    value = coalesce(var.postgres_admin_password, random_password.pg.result)
  }
  secret {
    name  = "redis-password"
    value = azurerm_redis_cache.reservation.primary_access_key
  }
  secret {
    name  = "sb-username"
    value = azurerm_servicebus_namespace.orders.name
  }
  secret {
    name  = "sb-password"
    value = azurerm_servicebus_namespace.orders.default_primary_key
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

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }
}

# Worker shares the Service Bus contract; scale-to-zero friendly.
# Image trỏ về cùng payment-service; nếu không dùng worker, hãy xóa khối này.
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
        name  = "SPRING_DATA_REDIS_HOST"
        value = azurerm_redis_cache.reservation.hostname
      }
      env {
        name  = "SPRING_DATA_REDIS_PORT"
        value = "6380"
      }
      env {
        name  = "SPRING_DATA_REDIS_SSL_ENABLED"
        value = "true"
      }
      env {
        name        = "SPRING_DATA_REDIS_PASSWORD"
        secret_name = "redis-password"
      }
      env {
        name  = "SPRING_RABBITMQ_HOST"
        value = "${azurerm_servicebus_namespace.orders.name}.servicebus.windows.net"
      }
      env {
        name  = "SPRING_RABBITMQ_PORT"
        value = "5672"
      }
      env {
        name        = "SPRING_RABBITMQ_USERNAME"
        secret_name = "sb-username"
      }
      env {
        name        = "SPRING_RABBITMQ_PASSWORD"
        secret_name = "sb-password"
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
    name  = "redis-password"
    value = azurerm_redis_cache.reservation.primary_access_key
  }
  secret {
    name  = "sb-username"
    value = azurerm_servicebus_namespace.orders.name
  }
  secret {
    name  = "sb-password"
    value = azurerm_servicebus_namespace.orders.default_primary_key
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


