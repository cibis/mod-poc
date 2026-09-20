#!/usr/bin/env bash
# Destroys the MOD PoC Azure environment (idempotent).
# Usage: down.sh [--prefix <p>] [--production-location <r>] [--yes]
#                [--controller-only | --production-only]
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/common.sh"

PREFIX="$DEFAULT_PREFIX"
PRODUCTION_LOCATION="$DEFAULT_PRODUCTION_LOCATION"
YES=false
CONTROLLER_ONLY=false
PRODUCTION_ONLY=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix)              PREFIX="$2";              shift 2 ;;
    --production-location) PRODUCTION_LOCATION="$2"; shift 2 ;;
    --yes)                 YES=true;                 shift   ;;
    --controller-only)     CONTROLLER_ONLY=true;     shift   ;;
    --production-only)     PRODUCTION_ONLY=true;     shift   ;;
    *) die "Unknown argument: $1" ;;
  esac
done

[[ "$CONTROLLER_ONLY" == "true" && "$PRODUCTION_ONLY" == "true" ]] \
  && die "--controller-only and --production-only are mutually exclusive"

require_az_login

confirm "DELETE resource groups for prefix '${PREFIX}'? This is irreversible."

# Resolve Key Vault name before the group is gone (needed for purge)
KV_NAME=""
if [[ "$CONTROLLER_ONLY" == "false" ]]; then
  STATE_FILE="$STATE_DIR/${PREFIX}.json"
  if [[ -f "$STATE_FILE" ]]; then
    KV_NAME=$(jq -r '.kvName // empty' "$STATE_FILE")
  fi
  if [[ -z "$KV_NAME" ]]; then
    KV_NAME=$(az keyvault list \
      --resource-group "$(rg_prod)" \
      --query "[0].name" -o tsv 2>/dev/null || true)
  fi
fi

# Delete controller group
if [[ "$PRODUCTION_ONLY" == "false" ]]; then
  if az group show --name "$(rg_ctrl)" --output none 2>/dev/null; then
    info "Deleting $(rg_ctrl)..."
    az group delete --name "$(rg_ctrl)" --yes --no-wait
  else
    info "$(rg_ctrl) not found — skipping"
  fi
fi

# Delete production group
if [[ "$CONTROLLER_ONLY" == "false" ]]; then
  if az group show --name "$(rg_prod)" --output none 2>/dev/null; then
    info "Deleting $(rg_prod)..."
    az group delete --name "$(rg_prod)" --yes --no-wait
  else
    info "$(rg_prod) not found — skipping"
  fi
fi

# Wait for group deletions to complete
if [[ "$PRODUCTION_ONLY" == "false" ]]; then
  az group wait --name "$(rg_ctrl)" --deleted 2>/dev/null || true
fi
if [[ "$CONTROLLER_ONLY" == "false" ]]; then
  az group wait --name "$(rg_prod)" --deleted 2>/dev/null || true
fi

# Purge soft-deleted Key Vault (Key Vault soft-delete is always on)
if [[ "$CONTROLLER_ONLY" == "false" && -n "$KV_NAME" ]]; then
  info "Purging soft-deleted Key Vault $KV_NAME..."
  az keyvault purge \
    --name "$KV_NAME" \
    --location "$PRODUCTION_LOCATION" \
    --no-wait 2>/dev/null || true
fi

info "Done."
