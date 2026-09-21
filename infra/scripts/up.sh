#!/usr/bin/env bash
# Creates or updates the full MOD PoC Azure environment (idempotent).
# Usage: up.sh [--prefix <p>] [--production-location <r>] [--controller-location <r>]
#              [--yes] [--developer <upn>] [--no-developer]
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/lib/common.sh"

# ── Tee output to timestamped log ─────────────────────────────────────────────
_TS="$(date +%Y%m%d-%H%M%S)"
_BUILD_LOG_DIR="$REPO_ROOT/tmp/up-$_TS"
_LOG_FILE="$_BUILD_LOG_DIR/up-$_TS.log"
mkdir -p "$_BUILD_LOG_DIR"
exec > >(tee "$_LOG_FILE") 2>&1
echo "Logging to $_LOG_FILE"

# ── Arg parsing ──────────────────────────────────────────────────────────────

PREFIX="$DEFAULT_PREFIX"
PRODUCTION_LOCATION="$DEFAULT_PRODUCTION_LOCATION"
CONTROLLER_LOCATION="$DEFAULT_CONTROLLER_LOCATION"
YES=false
DEVELOPERS=()
NO_DEVELOPER=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix)              PREFIX="$2";              shift 2 ;;
    --production-location) PRODUCTION_LOCATION="$2"; shift 2 ;;
    --controller-location) CONTROLLER_LOCATION="$2"; shift 2 ;;
    --yes)                 YES=true;                 shift   ;;
    --developer)           DEVELOPERS+=("$2");       shift 2 ;;
    --no-developer)        NO_DEVELOPER=true;        shift   ;;
    *) die "Unknown argument: $1" ;;
  esac
done

[[ "$PRODUCTION_LOCATION" != "$CONTROLLER_LOCATION" ]] \
  || die "--production-location and --controller-location must differ"

# ── Step 1: Prerequisites ─────────────────────────────────────────────────────

require_tool az
require_tool jq
require_az_login
az bicep version --output none 2>/dev/null || die "Bicep not installed. Run: az bicep install"
resolve_subscription
SUFFIX=$(compute_suffix "$SUBSCRIPTION_ID" "$PREFIX")

if [[ "$NO_DEVELOPER" == "false" && ${#DEVELOPERS[@]} -eq 0 ]]; then
  SIGNED_IN_UPN=$(az account show --query user.name -o tsv)
  DEVELOPERS=("$SIGNED_IN_UPN")
fi

# ── Step 2: Confirm ───────────────────────────────────────────────────────────

cat >&2 <<EOF

  Subscription : $SUBSCRIPTION_NAME ($SUBSCRIPTION_ID)
  Production   : $(rg_prod) in $PRODUCTION_LOCATION
  Controller   : $(rg_ctrl) in $CONTROLLER_LOCATION
  Prefix/Suffix: $PREFIX / $SUFFIX

EOF
confirm "Deploy MOD PoC to subscription '$SUBSCRIPTION_NAME'?"

# ── Step 3: Resource groups ───────────────────────────────────────────────────

info "Creating resource groups..."
az group create --name "$(rg_prod)" --location "$PRODUCTION_LOCATION" --output none
az group create --name "$(rg_ctrl)" --location "$CONTROLLER_LOCATION" --output none

# ── Step 3b: Register providers ───────────────────────────────────────────────

info "Registering providers (background)..."
register_providers

# ── Step 4: Deploy production/core.bicep ─────────────────────────────────────

info "Deploying production/core.bicep..."
CORE_OUT=$(az deployment group create \
  --resource-group "$(rg_prod)" \
  --name "core-${PREFIX}" \
  --template-file "$SCRIPT_DIR/../production/core.bicep" \
  --parameters prefix="$PREFIX" suffix="$SUFFIX" location="$PRODUCTION_LOCATION" deployerObjectId="$(az ad signed-in-user show --query id -o tsv 2>/dev/null || true)" \
  --query properties.outputs \
  --output json)

KV_NAME=$(                    printf '%s' "$CORE_OUT" | jq -r '.kvName.value')
KV_URI=$(                     printf '%s' "$CORE_OUT" | jq -r '.kvUri.value')
ACR_LOGIN_SERVER=$(            printf '%s' "$CORE_OUT" | jq -r '.acrLoginServer.value')
CAE_ID=$(                     printf '%s' "$CORE_OUT" | jq -r '.caeId.value')
CAE_NAME=$(                   printf '%s' "$CORE_OUT" | jq -r '.caeName.value')
CAE_DEFAULT_DOMAIN=$(          printf '%s' "$CORE_OUT" | jq -r '.caeDefaultDomain.value')
SQL_CONNECTION_STRING=$(       printf '%s' "$CORE_OUT" | jq -r '.sqlConnectionString.value')
EH_FQDN=$(                    printf '%s' "$CORE_OUT" | jq -r '.ehFqdn.value')
CMD_SB_FQDN=$(                 printf '%s' "$CORE_OUT" | jq -r '.cmdSbFqdn.value')
SIM_SB_FQDN=$(                 printf '%s' "$CORE_OUT" | jq -r '.simSbFqdn.value')
STORAGE_ACCOUNT_NAME=$(        printf '%s' "$CORE_OUT" | jq -r '.storageAccountName.value')
CHECKPOINT_BLOB_CONTAINER_URL=$(printf '%s' "$CORE_OUT" | jq -r '.checkpointBlobContainerUrl.value')
APP_INSIGHTS_CONNECTION_STRING=$(printf '%s' "$CORE_OUT" | jq -r '.appInsightsConnectionString.value')
INGEST_IDENTITY_ID=$(          printf '%s' "$CORE_OUT" | jq -r '.ingestIdentityId.value')
INGEST_CLIENT_ID=$(            printf '%s' "$CORE_OUT" | jq -r '.ingestClientId.value')
INGEST_PRINCIPAL_ID=$(         printf '%s' "$CORE_OUT" | jq -r '.ingestPrincipalId.value')
PROCESSING_IDENTITY_ID=$(      printf '%s' "$CORE_OUT" | jq -r '.processingIdentityId.value')
PROCESSING_CLIENT_ID=$(        printf '%s' "$CORE_OUT" | jq -r '.processingClientId.value')
PROCESSING_PRINCIPAL_ID=$(     printf '%s' "$CORE_OUT" | jq -r '.processingPrincipalId.value')
MANAGEMENT_IDENTITY_ID=$(      printf '%s' "$CORE_OUT" | jq -r '.managementIdentityId.value')
MANAGEMENT_CLIENT_ID=$(        printf '%s' "$CORE_OUT" | jq -r '.managementClientId.value')
MANAGEMENT_PRINCIPAL_ID=$(     printf '%s' "$CORE_OUT" | jq -r '.managementPrincipalId.value')
PORTAL_IDENTITY_ID=$(          printf '%s' "$CORE_OUT" | jq -r '.portalIdentityId.value')
PORTAL_CLIENT_ID=$(            printf '%s' "$CORE_OUT" | jq -r '.portalClientId.value')
PORTAL_PRINCIPAL_ID=$(         printf '%s' "$CORE_OUT" | jq -r '.portalPrincipalId.value')
REPORTING_IDENTITY_ID=$(       printf '%s' "$CORE_OUT" | jq -r '.reportingIdentityId.value')
REPORTING_CLIENT_ID=$(         printf '%s' "$CORE_OUT" | jq -r '.reportingClientId.value')
REPORTING_PRINCIPAL_ID=$(      printf '%s' "$CORE_OUT" | jq -r '.reportingPrincipalId.value')
MIGRATOR_IDENTITY_ID=$(        printf '%s' "$CORE_OUT" | jq -r '.migratorIdentityId.value')
MIGRATOR_CLIENT_ID=$(          printf '%s' "$CORE_OUT" | jq -r '.migratorClientId.value')
CMD_SB_ISSUER_KEY_SECRET_URI=$( printf '%s' "$CORE_OUT" | jq -r '.cmdSbIssuerKeySecretUri.value')
SIM_SB_ISSUER_KEY_SECRET_URI=$( printf '%s' "$CORE_OUT" | jq -r '.simSbIssuerKeySecretUri.value')
PORTAL_JWT_KEY_SECRET_URI=$(    printf '%s' "$CORE_OUT" | jq -r '.portalJwtKeySecretUri.value')
REPORTING_JWT_KEY_SECRET_URI=$( printf '%s' "$CORE_OUT" | jq -r '.reportingJwtKeySecretUri.value')
SQL_SERVER_PRINCIPAL_ID=$(      printf '%s' "$CORE_OUT" | jq -r '.sqlServerPrincipalId.value')

ACR_NAME="${ACR_LOGIN_SERVER%%.*}"

# ── Step 4b: Assign Directory Reader to SQL server identity ──────────────────
# Required so SQL Server can resolve Azure AD object IDs when creating users.

info "Assigning Directory Reader role to SQL server identity..."
_DR_TEMPLATE_ID="88d8e3e3-8f55-4a1e-953a-9b9898b8876b"
# Activate role in tenant if not already active (idempotent)
az rest --method POST \
  --url "https://graph.microsoft.com/v1.0/directoryRoles" \
  --body "{\"roleTemplateId\":\"${_DR_TEMPLATE_ID}\"}" \
  --output none 2>/dev/null || true
_DR_ROLE_ID=$(az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/directoryRoles?\$filter=roleTemplateId+eq+'${_DR_TEMPLATE_ID}'" \
  --query "value[0].id" -o tsv 2>/dev/null || true)
if [[ -n "$_DR_ROLE_ID" && -n "$SQL_SERVER_PRINCIPAL_ID" ]]; then
  az rest --method POST \
    --url "https://graph.microsoft.com/v1.0/directoryRoles/${_DR_ROLE_ID}/members/\$ref" \
    --body "{\"@odata.id\":\"https://graph.microsoft.com/v1.0/directoryObjects/${SQL_SERVER_PRINCIPAL_ID}\"}" \
    --output none 2>/dev/null || true
  info "Directory Reader assigned (or already present)."
else
  warn "Could not assign Directory Reader to SQL server — may need to be done manually."
fi

# ── Step 5: Key Vault secrets ─────────────────────────────────────────────────

KV_SCOPE="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$(rg_prod)/providers/Microsoft.KeyVault/vaults/$KV_NAME"

create_secret_if_absent() {
  local vault="$1" name="$2" value="$3"
  az keyvault secret show --vault-name "$vault" --name "$name" \
    --query id -o tsv 2>/dev/null | grep -q . && return 0
  local attempt
  for attempt in 1 2 3 4 5; do
    az keyvault secret set --vault-name "$vault" --name "$name" \
      --value "$value" --output none 2>/dev/null && return 0
    warn "Secret '$name' write attempt $attempt failed (RBAC propagation delay); retrying in 30 s..."
    sleep 30
  done
  die "Failed to write secret '$name' after 5 attempts"
}

info "Creating Key Vault secrets (if absent)..."
create_secret_if_absent "$KV_NAME" "portal-jwt-key" \
  "$(head -c 32 /dev/urandom | base64 -w0)"
create_secret_if_absent "$KV_NAME" "reporting-jwt-key" \
  "$(head -c 32 /dev/urandom | base64 -w0)"
create_secret_if_absent "$KV_NAME" "admin-password" \
  "$(LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | fold -w 20 | head -n 1)"
create_secret_if_absent "$KV_NAME" "customer-password" \
  "$(LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | fold -w 20 | head -n 1)"

# ── Step 6: Collector CA certificate ─────────────────────────────────────────

create_collector_ca_if_absent() {
  local vault="$1"
  # Key Vault Self-signed certificates do not reliably set cA=TRUE in the BasicConstraints
  # extension on EC keys. We generate the CA using openssl and store it as a secret (PKCS12
  # base64) so the management API can load it via X509CertificateLoader.LoadPkcs12.
  # Idempotency: skip if the secret already exists and is non-empty.
  local existing
  existing=$(az keyvault secret show --vault-name "$vault" --name collector-ca \
    --query "value" -o tsv 2>/dev/null || true)
  [[ -n "$existing" ]] && return 0

  info "Creating collector CA certificate (openssl, cA=TRUE)..."
  local tmp_dir
  tmp_dir=$(mktemp -d)
  trap "rm -rf '$tmp_dir'" RETURN

  # Generate EC P-256 key and self-signed CA cert with cA=TRUE
  openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 \
    -out "$tmp_dir/ca-key.pem" 2>/dev/null
  openssl req -new -x509 -days 730 \
    -key "$tmp_dir/ca-key.pem" \
    -out "$tmp_dir/ca-cert.pem" \
    -subj "//CN=MOD PoC Collector CA" \
    -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
    -addext "keyUsage=critical,keyCertSign,cRLSign,digitalSignature" \
    2>/dev/null

  # Convert to PKCS12 (no password) and base64-encode for Key Vault secret storage
  openssl pkcs12 -export -passout pass: \
    -inkey "$tmp_dir/ca-key.pem" \
    -in "$tmp_dir/ca-cert.pem" \
    -out "$tmp_dir/ca.pfx" 2>/dev/null
  local pfx_b64
  pfx_b64=$(base64 -w0 < "$tmp_dir/ca.pfx")

  az keyvault secret set \
    --vault-name "$vault" --name collector-ca \
    --value "$pfx_b64" \
    --content-type "application/x-pkcs12" \
    --output none
}

info "Ensuring collector CA certificate..."
create_collector_ca_if_absent "$KV_NAME"
# Read the CA public certificate from the secret (PKCS12 base64) for use in Bicep parameters.
# openssl pkcs12 -nokeys -noout ... extracts the public cert PEM.
_kv_secret=$(az keyvault secret show --vault-name "$KV_NAME" --name collector-ca \
  --query value -o tsv 2>/dev/null)
_pfx_tmp=$(mktemp)
echo "$_kv_secret" | base64 -d > "$_pfx_tmp"
_ca_cert_pem=$(openssl pkcs12 -in "$_pfx_tmp" -nokeys -passin pass: 2>/dev/null \
  | grep -v "^MAC\|^Bag\|^Bag Attr\|^subject\|^issuer\|friendlyName")
rm -f "$_pfx_tmp"
COLLECTOR_CA_PEM_B64=$(printf '%s' "$_ca_cert_pem" | base64 -w0)

# ── Step 7: Developer access ──────────────────────────────────────────────────

DEVELOPER_PRINCIPALS=""

grant_developer_access() {
  local upn="$1"
  local obj_id
  obj_id=$(az ad user show --id "$upn" --query id -o tsv 2>/dev/null) \
    || { warn "Could not resolve object ID for $upn; skipping."; return 0; }

  local prod_rg_scope="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$(rg_prod)"
  local ctrl_rg_scope="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$(rg_ctrl)"
  local eh_ns="${EH_FQDN%%.*}"
  local cmd_sb_ns="${CMD_SB_FQDN%%.*}"
  local sim_sb_ns="${SIM_SB_FQDN%%.*}"
  local eh_scope="$prod_rg_scope/providers/Microsoft.EventHub/namespaces/$eh_ns"
  local cmd_sb_scope="$prod_rg_scope/providers/Microsoft.ServiceBus/namespaces/$cmd_sb_ns"
  local sim_sb_scope="$prod_rg_scope/providers/Microsoft.ServiceBus/namespaces/$sim_sb_ns"
  local storage_scope="$prod_rg_scope/providers/Microsoft.Storage/storageAccounts/$STORAGE_ACCOUNT_NAME"

  local args=(--assignee-object-id "$obj_id" --assignee-principal-type User --output none)

  az role assignment create --role "Key Vault Secrets User" \
    --scope "$KV_SCOPE" "${args[@]}" 2>/dev/null || true
  az role assignment create --role "Azure Event Hubs Data Sender" \
    --scope "$eh_scope" "${args[@]}" 2>/dev/null || true
  az role assignment create --role "Azure Event Hubs Data Receiver" \
    --scope "$eh_scope" "${args[@]}" 2>/dev/null || true
  az role assignment create --role "Azure Service Bus Data Owner" \
    --scope "$cmd_sb_scope" "${args[@]}" 2>/dev/null || true
  az role assignment create --role "Azure Service Bus Data Owner" \
    --scope "$sim_sb_scope" "${args[@]}" 2>/dev/null || true
  az role assignment create --role "Storage Blob Data Contributor" \
    --scope "$storage_scope" "${args[@]}" 2>/dev/null || true
  az role assignment create --role "Reader" \
    --scope "$prod_rg_scope" "${args[@]}" 2>/dev/null || true
  az role assignment create --role "Contributor" \
    --scope "$ctrl_rg_scope" "${args[@]}" 2>/dev/null || true

  DEVELOPER_PRINCIPALS="${DEVELOPER_PRINCIPALS:+$DEVELOPER_PRINCIPALS,}${upn}:${obj_id}"
  info "Granted developer access to $upn"
}

if [[ "$NO_DEVELOPER" == "false" ]]; then
  for _upn in "${DEVELOPERS[@]}"; do
    grant_developer_access "$_upn"
  done
fi

# ── Step 8: Build images in parallel ─────────────────────────────────────────

IMAGE_TAG=$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null \
  || date -u +%Y%m%d%H%M%S)
info "Building images with tag: $IMAGE_TAG"

declare -a BUILD_PIDS=()
declare -a BUILD_LOGS=()
declare -a BUILD_IMAGES=()

_start_acr_build() {
  local image="$1"; shift
  local imgname="${image%%:*}"; imgname="${imgname##*/}"
  local log="$_BUILD_LOG_DIR/acr-$imgname-$IMAGE_TAG.log"
  # Run without log streaming to avoid Windows colorama/CP1252 encoding crash.
  # Capture run ID so logs can be fetched after completion.
  (
    local output exit_code run_id
    output=$(az acr build --registry "$ACR_NAME" --image "$image" \
      --no-logs --output json "$@" 2>&1)
    exit_code=$?
    run_id=$(printf '%s' "$output" | jq -r '.runId // empty' 2>/dev/null || true)
    [[ -z "$run_id" ]] && \
      run_id=$(printf '%s' "$output" | grep -oE 'build with ID: [a-z0-9]+' | awk '{print $NF}' || true)
    printf '%s\n' "$run_id" >"${log%.log}.runid"
    exit $exit_code
  ) &
  BUILD_PIDS+=($!)
  BUILD_LOGS+=("$log")
  BUILD_IMAGES+=("$image")
}

_start_acr_build "mod/ingest-api:$IMAGE_TAG" \
  --file "platform/docker/ingest-api.Dockerfile" "$REPO_ROOT/platform"

_start_acr_build "mod/management-api:$IMAGE_TAG" \
  --file "platform/docker/management-api.Dockerfile" "$REPO_ROOT/platform"

_start_acr_build "mod/processing:$IMAGE_TAG" \
  --file "platform/docker/processing.Dockerfile" "$REPO_ROOT/platform"

_start_acr_build "mod/db-migrator:$IMAGE_TAG" \
  --file "platform/docker/db-migrator.Dockerfile" "$REPO_ROOT/platform"

_start_acr_build "mod/collector:$IMAGE_TAG" "$REPO_ROOT/edge"

_start_acr_build "mod/portal:$IMAGE_TAG" "$REPO_ROOT/portal"

_start_acr_build "mod/customer:$IMAGE_TAG" "$REPO_ROOT/customer"

info "Waiting for all image builds..."
_build_failed=false
for _i in "${!BUILD_PIDS[@]}"; do
  _exit=0
  wait "${BUILD_PIDS[$_i]}" || _exit=$?
  _run_id=$(cat "${BUILD_LOGS[$_i]%.log}.runid" 2>/dev/null | tr -d '[:space:]' || true)
  if [[ -n "$_run_id" ]]; then
    info "Fetching build log for ${BUILD_IMAGES[$_i]} (run $_run_id)..."
    az acr task logs --registry "$ACR_NAME" --run-id "$_run_id" \
      >"${BUILD_LOGS[$_i]}" 2>&1 || true
  fi
  if [[ $_exit -ne 0 ]]; then
    error "Build failed: ${BUILD_IMAGES[$_i]} — see ${BUILD_LOGS[$_i]}"
    [[ -f "${BUILD_LOGS[$_i]}" ]] && cat "${BUILD_LOGS[$_i]}" >&2
    _build_failed=true
  else
    info "Build succeeded: ${BUILD_IMAGES[$_i]} — log: ${BUILD_LOGS[$_i]}"
  fi
done
[[ "$_build_failed" == "true" ]] && die "One or more image builds failed."
info "All images built and pushed."

# ── Step 9: Deploy controller/main.bicep ─────────────────────────────────────

info "Deploying controller/main.bicep..."
CTRL_OUT=$(az deployment group create \
  --resource-group "$(rg_ctrl)" \
  --name "ctrl-${PREFIX}" \
  --template-file "$SCRIPT_DIR/../controller/main.bicep" \
  --parameters \
    prefix="$PREFIX" \
    suffix="$SUFFIX" \
    location="$CONTROLLER_LOCATION" \
    productionResourceGroup="$(rg_prod)" \
    portalPrincipalId="$PORTAL_PRINCIPAL_ID" \
  --query properties.outputs \
  --output json)

CTRL_CAE_ID=$(              printf '%s' "$CTRL_OUT" | jq -r '.caeId.value')
CTRL_CAE_DEFAULT_DOMAIN=$(   printf '%s' "$CTRL_OUT" | jq -r '.caeDefaultDomain.value')
COLLECTOR_PULL_IDENTITY_ID=$(printf '%s' "$CTRL_OUT" | jq -r '.collectorPullIdentityId.value')

# ── Step 10: Deploy production/apps.bicep ────────────────────────────────────

info "Deploying production/apps.bicep..."
_apps_bicep=$(cygpath -w "$SCRIPT_DIR/../production/apps.bicep" 2>/dev/null \
  || echo "$SCRIPT_DIR/../production/apps.bicep")
MSYS_NO_PATHCONV=1 az deployment group create \
  --resource-group "$(rg_prod)" \
  --name "apps-${PREFIX}" \
  --template-file "$_apps_bicep" \
  --parameters \
    imageTag="$IMAGE_TAG" \
    caeId="$CAE_ID" \
    caeDefaultDomain="$CAE_DEFAULT_DOMAIN" \
    ingestIdentityId="$INGEST_IDENTITY_ID" \
    ingestClientId="$INGEST_CLIENT_ID" \
    processingIdentityId="$PROCESSING_IDENTITY_ID" \
    processingClientId="$PROCESSING_CLIENT_ID" \
    managementIdentityId="$MANAGEMENT_IDENTITY_ID" \
    managementClientId="$MANAGEMENT_CLIENT_ID" \
    portalIdentityId="$PORTAL_IDENTITY_ID" \
    portalClientId="$PORTAL_CLIENT_ID" \
    reportingIdentityId="$REPORTING_IDENTITY_ID" \
    reportingClientId="$REPORTING_CLIENT_ID" \
    migratorIdentityId="$MIGRATOR_IDENTITY_ID" \
    migratorClientId="$MIGRATOR_CLIENT_ID" \
    ingestPrincipalId="$INGEST_PRINCIPAL_ID" \
    processingPrincipalId="$PROCESSING_PRINCIPAL_ID" \
    managementPrincipalId="$MANAGEMENT_PRINCIPAL_ID" \
    portalPrincipalId="$PORTAL_PRINCIPAL_ID" \
    reportingPrincipalId="$REPORTING_PRINCIPAL_ID" \
    appInsightsConnectionString="$APP_INSIGHTS_CONNECTION_STRING" \
    kvUri="$KV_URI" \
    acrLoginServer="$ACR_LOGIN_SERVER" \
    storageAccountName="$STORAGE_ACCOUNT_NAME" \
    checkpointBlobContainerUrl="$CHECKPOINT_BLOB_CONTAINER_URL" \
    ehFqdn="$EH_FQDN" \
    cmdSbFqdn="$CMD_SB_FQDN" \
    simSbFqdn="$SIM_SB_FQDN" \
    sqlConnectionString="$SQL_CONNECTION_STRING" \
    cmdSbIssuerKeySecretUri="$CMD_SB_ISSUER_KEY_SECRET_URI" \
    simSbIssuerKeySecretUri="$SIM_SB_ISSUER_KEY_SECRET_URI" \
    portalJwtKeySecretUri="$PORTAL_JWT_KEY_SECRET_URI" \
    reportingJwtKeySecretUri="$REPORTING_JWT_KEY_SECRET_URI" \
    controllerSubscriptionId="$SUBSCRIPTION_ID" \
    controllerResourceGroup="$(rg_ctrl)" \
    controllerEnvironmentId="$CTRL_CAE_ID" \
    controllerLocation="$CONTROLLER_LOCATION" \
    collectorPullIdentityId="$COLLECTOR_PULL_IDENTITY_ID" \
    developerPrincipals="$DEVELOPER_PRINCIPALS" \
    collectorCaPemB64="$COLLECTOR_CA_PEM_B64" \
  --output none

# ── Step 11: Run database migrator job ───────────────────────────────────────

info "Starting database migration job..."
EXEC_NAME=$(az containerapp job start \
  --name job-dbmigrate \
  --resource-group "$(rg_prod)" \
  --query name -o tsv)

JOB_STATUS=""
for _i in $(seq 1 60); do
  JOB_STATUS=$(az containerapp job execution show \
    --name job-dbmigrate \
    --resource-group "$(rg_prod)" \
    --job-execution-name "$EXEC_NAME" \
    --query properties.status -o tsv 2>/dev/null || echo "Unknown")
  case "$JOB_STATUS" in
    Succeeded) break ;;
    Failed|Degraded)
      error "Migration job failed (status: $JOB_STATUS). Logs:"
      az containerapp logs show \
        --name job-dbmigrate \
        --resource-group "$(rg_prod)" \
        --type console --tail 50 2>/dev/null || true
      exit 1 ;;
  esac
  sleep 10
done
[[ "$JOB_STATUS" == "Succeeded" ]] || die "Migration job timed out (last status: $JOB_STATUS)"
info "Migration complete."

# ── Step 12: Write state file ─────────────────────────────────────────────────

info "Querying deployed app FQDNs..."
PORTAL_FQDN=$(az containerapp show \
  --name ca-portal --resource-group "$(rg_prod)" \
  --query properties.configuration.ingress.fqdn -o tsv)
REPORTING_FQDN=$(az containerapp show \
  --name ca-reporting --resource-group "$(rg_prod)" \
  --query properties.configuration.ingress.fqdn -o tsv)

mkdir -p "$STATE_DIR"
jq -n \
  --arg prefix                    "$PREFIX" \
  --arg suffix                    "$SUFFIX" \
  --arg prodRg                    "$(rg_prod)" \
  --arg ctrlRg                    "$(rg_ctrl)" \
  --arg prodLocation              "$PRODUCTION_LOCATION" \
  --arg ctrlLocation              "$CONTROLLER_LOCATION" \
  --arg kvName                    "$KV_NAME" \
  --arg kvUri                     "$KV_URI" \
  --arg acrLoginServer            "$ACR_LOGIN_SERVER" \
  --arg imageTag                  "$IMAGE_TAG" \
  --arg caeName                   "$CAE_NAME" \
  --arg caeId                     "$CAE_ID" \
  --arg caeDefaultDomain          "$CAE_DEFAULT_DOMAIN" \
  --arg ctrlCaeId                 "$CTRL_CAE_ID" \
  --arg ctrlCaeDefaultDomain      "$CTRL_CAE_DEFAULT_DOMAIN" \
  --arg ehFqdn                    "$EH_FQDN" \
  --arg cmdSbFqdn                 "$CMD_SB_FQDN" \
  --arg simSbFqdn                 "$SIM_SB_FQDN" \
  --arg storageAccountName        "$STORAGE_ACCOUNT_NAME" \
  --arg checkpointBlobContainerUrl "$CHECKPOINT_BLOB_CONTAINER_URL" \
  --arg appInsightsConnectionString "$APP_INSIGHTS_CONNECTION_STRING" \
  --arg sqlConnectionString       "$SQL_CONNECTION_STRING" \
  --arg ingestIdentityId          "$INGEST_IDENTITY_ID" \
  --arg ingestClientId            "$INGEST_CLIENT_ID" \
  --arg ingestPrincipalId         "$INGEST_PRINCIPAL_ID" \
  --arg processingIdentityId      "$PROCESSING_IDENTITY_ID" \
  --arg processingClientId        "$PROCESSING_CLIENT_ID" \
  --arg processingPrincipalId     "$PROCESSING_PRINCIPAL_ID" \
  --arg managementIdentityId      "$MANAGEMENT_IDENTITY_ID" \
  --arg managementClientId        "$MANAGEMENT_CLIENT_ID" \
  --arg managementPrincipalId     "$MANAGEMENT_PRINCIPAL_ID" \
  --arg portalIdentityId          "$PORTAL_IDENTITY_ID" \
  --arg portalClientId            "$PORTAL_CLIENT_ID" \
  --arg portalPrincipalId         "$PORTAL_PRINCIPAL_ID" \
  --arg reportingIdentityId       "$REPORTING_IDENTITY_ID" \
  --arg reportingClientId         "$REPORTING_CLIENT_ID" \
  --arg reportingPrincipalId      "$REPORTING_PRINCIPAL_ID" \
  --arg migratorIdentityId        "$MIGRATOR_IDENTITY_ID" \
  --arg migratorClientId          "$MIGRATOR_CLIENT_ID" \
  --arg cmdSbIssuerKeySecretUri   "$CMD_SB_ISSUER_KEY_SECRET_URI" \
  --arg simSbIssuerKeySecretUri   "$SIM_SB_ISSUER_KEY_SECRET_URI" \
  --arg portalJwtKeySecretUri     "$PORTAL_JWT_KEY_SECRET_URI" \
  --arg reportingJwtKeySecretUri  "$REPORTING_JWT_KEY_SECRET_URI" \
  --arg collectorPullIdentityId   "$COLLECTOR_PULL_IDENTITY_ID" \
  --arg collectorCaPemB64         "$COLLECTOR_CA_PEM_B64" \
  --arg portalFqdn                "$PORTAL_FQDN" \
  --arg reportingFqdn             "$REPORTING_FQDN" \
  '{
    prefix:                    $prefix,
    suffix:                    $suffix,
    prodRg:                    $prodRg,
    ctrlRg:                    $ctrlRg,
    prodLocation:              $prodLocation,
    ctrlLocation:              $ctrlLocation,
    kvName:                    $kvName,
    kvUri:                     $kvUri,
    acrLoginServer:            $acrLoginServer,
    imageTag:                  $imageTag,
    caeName:                   $caeName,
    caeId:                     $caeId,
    caeDefaultDomain:          $caeDefaultDomain,
    ctrlCaeId:                 $ctrlCaeId,
    ctrlCaeDefaultDomain:      $ctrlCaeDefaultDomain,
    ehFqdn:                    $ehFqdn,
    cmdSbFqdn:                 $cmdSbFqdn,
    simSbFqdn:                 $simSbFqdn,
    storageAccountName:        $storageAccountName,
    checkpointBlobContainerUrl: $checkpointBlobContainerUrl,
    appInsightsConnectionString: $appInsightsConnectionString,
    sqlConnectionString:       $sqlConnectionString,
    ingestIdentityId:          $ingestIdentityId,
    ingestClientId:            $ingestClientId,
    ingestPrincipalId:         $ingestPrincipalId,
    processingIdentityId:      $processingIdentityId,
    processingClientId:        $processingClientId,
    processingPrincipalId:     $processingPrincipalId,
    managementIdentityId:      $managementIdentityId,
    managementClientId:        $managementClientId,
    managementPrincipalId:     $managementPrincipalId,
    portalIdentityId:          $portalIdentityId,
    portalClientId:            $portalClientId,
    portalPrincipalId:         $portalPrincipalId,
    reportingIdentityId:       $reportingIdentityId,
    reportingClientId:         $reportingClientId,
    reportingPrincipalId:      $reportingPrincipalId,
    migratorIdentityId:        $migratorIdentityId,
    migratorClientId:          $migratorClientId,
    cmdSbIssuerKeySecretUri:   $cmdSbIssuerKeySecretUri,
    simSbIssuerKeySecretUri:   $simSbIssuerKeySecretUri,
    portalJwtKeySecretUri:     $portalJwtKeySecretUri,
    reportingJwtKeySecretUri:  $reportingJwtKeySecretUri,
    collectorPullIdentityId:   $collectorPullIdentityId,
    collectorCaPemB64:         $collectorCaPemB64,
    portalFqdn:                $portalFqdn,
    reportingFqdn:             $reportingFqdn
  }' > "$STATE_DIR/${PREFIX}.json"

info "State written to $STATE_DIR/${PREFIX}.json"

# ── Step 13: Ready-to-demo summary ────────────────────────────────────────────

cat <<EOF

===============================================================
  MOD PoC ready
===============================================================
  Admin portal   : https://${PORTAL_FQDN}
  Customer portal: https://${REPORTING_FQDN}

  Users:
    admin              (ModAdmin)
    customer-a-viewer  (Customer A)
    customer-b-viewer  (Customer B)
    customer-c-viewer  (Customer C)

  Retrieve passwords:
    az keyvault secret show \\
      --vault-name ${KV_NAME} --name admin-password \\
      --query value -o tsv
    az keyvault secret show \\
      --vault-name ${KV_NAME} --name customer-password \\
      --query value -o tsv

  Seeded tenants (8 collectors: 4 EU-North, 4 EU-South):
    Customer A: 3 sites (EU-North×2, EU-South×1), 3 collectors
    Customer B: 2 sites (EU-North×1, EU-South×1), 2 collectors
    Customer C: 2 sites (EU-North×1, EU-South×1), 3 collectors
                (Customer C South 1 has 2 line collectors)

  Next step: Admin portal → Simulator → Fleet → Power on all

  Tear down: bash infra/scripts/down.sh --prefix ${PREFIX}
===============================================================
EOF
