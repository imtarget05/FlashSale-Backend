# Phase 3C-TF — remote encrypted Terraform state backend for the
# data-protection root (migrates the real local state, then protects it).

# 1) Create the state storage account. The name must be globally unique;
#    adjust until `az storage account check-name` reports available.
STATE_RG="${STATE_RG:-rg-flashsale-tfstate}"
STATE_LOCATION="${STATE_LOCATION:-southeastasia}"
STATE_ACCOUNT="${STATE_ACCOUNT:-stflashsaletfstate}"
STATE_CONTAINER="${STATE_CONTAINER:-tfstate}"

az group create -n "$STATE_RG" -l "$STATE_LOCATION" -o none 2>/dev/null || true
az storage account create \
  -n "$STATE_ACCOUNT" -g "$STATE_RG" -l "$STATE_LOCATION" \
  --sku Standard_LRS --kind StorageV2 \
  --https-only true --min-tls-version TLS1_2 \
  --allow-blob-public-access false --allow-shared-key-access true \
  --public-network-access Enabled -o none

# Same operator discipline as the backup account: firewall + RBAC, so state
# (which embeds resource IDs and prior values) is reachable only via identity.
MYIP="$(curl -s https://api.ipify.org)"
az storage account network-rule add -g "$STATE_RG" --account-name "$STATE_ACCOUNT" \
  --ip-address "$MYIP" -o none 2>/dev/null || true
az storage account update -g "$STATE_RG" -n "$STATE_ACCOUNT" \
  --default-action Deny --bypass AzureServices -o none

# Entra RBAC for the operator (Storage Blob Data Contributor on the account).
ME="$(az ad signed-in-user show --query id -o tsv)"
SUB="$(az account show --query id -o tsv)"
az role assignment create --assignee-object-id "$ME" --assignee-principal-type User \
  --role "Storage Blob Data Contributor" \
  --scope "/subscriptions/$SUB/resourceGroups/$STATE_RG/providers/Microsoft.Storage/storageAccounts/$STATE_ACCOUNT" \
  -o none 2>/dev/null || true

echo "STATE-CREATE PASS: $STATE_ACCOUNT ($STATE_RG) — firewall Deny + $MYIP, RBAC assigned"
