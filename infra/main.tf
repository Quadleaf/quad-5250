# Quad5250 POC infra: ACR, Container Apps env, two apps (api internal,
# web external), Log Analytics. Single replica each — POC scoped to ~5
# concurrent users.

resource "random_string" "suffix" {
  length  = 6
  upper   = false
  special = false
  numeric = true
}

locals {
  prefix   = var.prefix
  rg_name  = "${local.prefix}-${var.environment_name}-rg"
  acr_name = lower(replace("${local.prefix}acr${random_string.suffix.result}", "-", ""))
  env_name = "${local.prefix}-env"
  la_name  = "${local.prefix}-logs"
  mi_name  = "${local.prefix}-mi"
  api_name = "${local.prefix}-api"
  web_name = "${local.prefix}-web"

  tags = {
    environment = var.environment_name
    app         = local.prefix
  }
}

resource "azurerm_resource_group" "this" {
  name     = local.rg_name
  location = var.location
  tags     = local.tags
}

resource "azurerm_log_analytics_workspace" "this" {
  name                = local.la_name
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.tags
}

resource "azurerm_container_registry" "this" {
  name                = local.acr_name
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  sku                 = "Basic"
  admin_enabled       = false
  tags                = local.tags
}

resource "azurerm_user_assigned_identity" "this" {
  name                = local.mi_name
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  tags                = local.tags
}

resource "azurerm_role_assignment" "acr_pull" {
  scope                = azurerm_container_registry.this.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

resource "azurerm_container_app_environment" "this" {
  name                       = local.env_name
  resource_group_name        = azurerm_resource_group.this.name
  location                   = azurerm_resource_group.this.location
  log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id
  tags                       = local.tags
}

resource "azurerm_container_app" "api" {
  name                         = local.api_name
  resource_group_name          = azurerm_resource_group.this.name
  container_app_environment_id = azurerm_container_app_environment.this.id
  revision_mode                = "Single"
  tags                         = merge(local.tags, { "azd-service-name" = "api" })

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.this.id]
  }

  registry {
    server   = azurerm_container_registry.this.login_server
    identity = azurerm_user_assigned_identity.this.id
  }

  ingress {
    external_enabled = false
    target_port      = 8080
    transport        = "auto"
    # Internal-only: nginx in the web app calls us over HTTP on the env's
    # internal FQDN. With insecure disabled, the env edge 301s to HTTPS,
    # which the browser would have to follow to a non-routable internal URL.
    allow_insecure_connections = true

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "api"
      image  = var.api_image
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_URLS"
        value = "http://+:8080"
      }
      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
    }
  }

  # azd's `deploy` step pushes new images and rolls the app via the ARM API,
  # outside of Terraform's view. Without this, every `terraform apply` reverts
  # the running image back to the placeholder default in var.api_image.
  lifecycle {
    ignore_changes = [
      template[0].container[0].image,
    ]
  }

  depends_on = [azurerm_role_assignment.acr_pull]
}

resource "azurerm_container_app" "web" {
  name                         = local.web_name
  resource_group_name          = azurerm_resource_group.this.name
  container_app_environment_id = azurerm_container_app_environment.this.id
  revision_mode                = "Single"
  tags                         = merge(local.tags, { "azd-service-name" = "web" })

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.this.id]
  }

  registry {
    server   = azurerm_container_registry.this.login_server
    identity = azurerm_user_assigned_identity.this.id
  }

  ingress {
    external_enabled           = true
    target_port                = 80
    transport                  = "auto"
    allow_insecure_connections = false

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "web"
      image  = var.web_image
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        # nginx envsubst rewrites this into proxy_pass at startup.
        name  = "API_HOST"
        value = azurerm_container_app.api.ingress[0].fqdn
      }
    }
  }

  lifecycle {
    ignore_changes = [
      template[0].container[0].image,
    ]
  }

  depends_on = [azurerm_role_assignment.acr_pull]
}
