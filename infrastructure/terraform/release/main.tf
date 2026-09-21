resource "azurerm_resource_group" "release" {
  name     = var.resource_group_name
  location = var.location
}

# Admin user DISABLED: the only push path is Entra identity (AcrPush), ADR-011.
# NOTE: retention_policy / quarantine_policy are Premium-only — on Standard the
# registry is cleaned with `az acr purge` (scheduled in Phase 7) instead.
resource "azurerm_container_registry" "acr" {
  name                          = var.acr_name
  resource_group_name           = azurerm_resource_group.release.name
  location                      = azurerm_resource_group.release.location
  sku                           = "Standard"
  admin_enabled                 = false
  public_network_access_enabled = true
}

# GitHub OIDC federation: the workflow's OIDC token is exchanged for this
# identity — no client secrets anywhere (ADR-011).
resource "azurerm_user_assigned_identity" "github_release" {
  name                = "id-flashsale-github-release"
  resource_group_name = azurerm_resource_group.release.name
  location            = azurerm_resource_group.release.location
}

resource "azurerm_federated_identity_credential" "main_branch" {
  name                = "gh-main"
  resource_group_name = azurerm_resource_group.release.name
  parent_id           = azurerm_user_assigned_identity.github_release.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"
  subject             = "repo:${var.github_org}/${var.github_repo}:ref:refs/heads/main"
}

resource "azurerm_federated_identity_credential" "pull_request" {
  name                = "gh-pr"
  resource_group_name = azurerm_resource_group.release.name
  parent_id           = azurerm_user_assigned_identity.github_release.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"
  subject             = "repo:${var.github_org}/${var.github_repo}:pull_request"
}

# ID-qualified subjects — the ones GitHub actually presents today (Phase 6
# finding: AADSTS700213 "no matching federated identity record" for the plain
# org/repo form because the assertion carried owner/repo numeric ids).
resource "azurerm_federated_identity_credential" "main_branch_ids" {
  name                = "gh-main-immutable-ids"
  resource_group_name = azurerm_resource_group.release.name
  parent_id           = azurerm_user_assigned_identity.github_release.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"
  subject             = "repo:${var.github_org}@${var.github_owner_id}/${var.github_repo}@${var.github_repo_id}:ref:refs/heads/main"
}

resource "azurerm_federated_identity_credential" "pull_request_ids" {
  name                = "gh-pr-immutable-ids"
  resource_group_name = azurerm_resource_group.release.name
  parent_id           = azurerm_user_assigned_identity.github_release.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"
  subject             = "repo:${var.github_org}@${var.github_owner_id}/${var.github_repo}@${var.github_repo_id}:pull_request"
}

# ---------------------------------------------------------------------------
# Phase 6B: the SAME portfolio identity also releases P02 (the productionized
# legacy app). One identity, one ACR, extra repo-scoped subjects — no second
# managed identity and no client secret (see P02 ADR "Decision" item 2).
# ---------------------------------------------------------------------------
resource "azurerm_federated_identity_credential" "legacy_main_branch_ids" {
  name                = "gh-legacy-main-immutable-ids"
  resource_group_name = azurerm_resource_group.release.name
  parent_id           = azurerm_user_assigned_identity.github_release.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"
  subject             = "repo:${var.github_org}@${var.github_owner_id}/${var.legacy_repo}@${var.legacy_repo_id}:ref:refs/heads/main"
}

resource "azurerm_federated_identity_credential" "legacy_pull_request_ids" {
  name                = "gh-legacy-pr-immutable-ids"
  resource_group_name = azurerm_resource_group.release.name
  parent_id           = azurerm_user_assigned_identity.github_release.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"
  subject             = "repo:${var.github_org}@${var.github_owner_id}/${var.legacy_repo}@${var.legacy_repo_id}:pull_request"
}

# AcrPush for SHA-tagged image publishes; AcrPull so the identity can also
# verify what it pushed (digest listing) in the same job.
resource "azurerm_role_assignment" "acr_push" {
  scope                = azurerm_container_registry.acr.id
  role_definition_name = "AcrPush"
  principal_id         = azurerm_user_assigned_identity.github_release.principal_id
}

resource "azurerm_role_assignment" "acr_pull" {
  scope                = azurerm_container_registry.acr.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.github_release.principal_id
}
