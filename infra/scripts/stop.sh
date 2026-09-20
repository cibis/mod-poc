#!/usr/bin/env bash
# Cold-stops the MOD PoC: scales production apps to 0, deletes controller collector apps.
# Usage: stop.sh [--prefix <p>] [--yes]
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/common.sh"

PREFIX="$DEFAULT_PREFIX"
YES=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix) PREFIX="$2"; shift 2 ;;
    --yes)    YES=true;    shift   ;;
    *) die "Unknown argument: $1" ;;
  esac
done

require_az_login

confirm "Scale down all production apps and remove collector apps in $(rg_ctrl) for prefix '$PREFIX'?"

# Scale all production container apps to 0 min replicas
PROD_APPS=(ca-ingest ca-mgmt ca-processing ca-portal ca-reporting)
declare -a SCALE_PIDS=()
for _app in "${PROD_APPS[@]}"; do
  info "Scaling $_app to 0 min replicas..."
  az containerapp update \
    --name "$_app" \
    --resource-group "$(rg_prod)" \
    --min-replicas 0 \
    --output none &
  SCALE_PIDS+=($!)
done
wait "${SCALE_PIDS[@]}"
info "Production apps scaled down."

# Delete all col-* apps from the controller group (portal reconciler marks them Off on restart)
mapfile -t COL_APPS < <(az containerapp list \
  --resource-group "$(rg_ctrl)" \
  --query "[?starts_with(name,'col-')].name" \
  -o tsv 2>/dev/null || true)

declare -a DEL_PIDS=()
for _app in "${COL_APPS[@]}"; do
  [[ -n "$_app" ]] || continue
  info "Deleting $_app..."
  az containerapp delete \
    --name "$_app" \
    --resource-group "$(rg_ctrl)" \
    --yes \
    --output none &
  DEL_PIDS+=($!)
done
[[ ${#DEL_PIDS[@]} -gt 0 ]] && wait "${DEL_PIDS[@]}"
info "Collector apps removed."

cat >&2 <<'BILLABLE'

Still billable after cold stop:
  - Event Hubs namespace (Throughput Units)
  - Container Registry (storage + task runs)
  - Key Vault (operations)
  - Storage account (capacity)
  - SQL (auto-pause applies after configured idle period)
  - Container Apps Environment (base charge)

BILLABLE

info "Cold stop complete."
