#!/usr/bin/env bash
# Shared helpers for infra scripts. Source with: source "$(dirname "$0")/lib/common.sh"

COMMON_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFRA_DIR="$(cd "$COMMON_DIR/../.." && pwd)"
REPO_ROOT="$(cd "$INFRA_DIR/.." && pwd)"
STATE_DIR="$INFRA_DIR/.state"

DEFAULT_PREFIX="modpoc"
DEFAULT_PRODUCTION_LOCATION="westeurope"
DEFAULT_CONTROLLER_LOCATION="northeurope"

info()  { echo "[INFO]  $*" >&2; }
warn()  { echo "[WARN]  $*" >&2; }
error() { echo "[ERROR] $*" >&2; }
die()   { error "$*"; exit 1; }

require_tool() {
  command -v "$1" &>/dev/null || die "Required tool not found: $1"
}

require_az_login() {
  az account show --output none 2>/dev/null || die "Not logged in to Azure. Run: az login"
}

resolve_subscription() {
  SUBSCRIPTION_ID=$(az account show --query id -o tsv)
  SUBSCRIPTION_NAME=$(az account show --query name -o tsv)
}

# First 5 hex chars of sha256(subscriptionId + prefix) — deterministic across runs
compute_suffix() {
  local sub="$1" pref="$2"
  printf '%s%s' "$sub" "$pref" | sha256sum | cut -c1-5
}

rg_prod() { echo "rg-${PREFIX}-prod"; }
rg_ctrl() { echo "rg-${PREFIX}-ctrl"; }

confirm() {
  [[ "${YES:-false}" == "true" ]] && return 0
  local prompt="${1:-Continue?}"
  local _ans
  read -r -p "$prompt [y/N] " _ans
  [[ "${_ans,,}" == "y" ]] || { info "Aborted."; exit 0; }
}

register_providers() {
  local providers=(
    Microsoft.App Microsoft.EventHub Microsoft.ServiceBus Microsoft.Sql
    Microsoft.KeyVault Microsoft.ContainerRegistry Microsoft.OperationalInsights
    Microsoft.Insights Microsoft.Storage
  )
  local pids=()
  for p in "${providers[@]}"; do
    az provider register --namespace "$p" --output none &
    pids+=($!)
  done
  wait "${pids[@]}"
}
