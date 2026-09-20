#!/usr/bin/env bash
# Restarts the MOD PoC after a cold stop by redeploying production/apps.bicep.
# Usage: start.sh [--prefix <p>] [--yes]
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
resolve_subscription

STATE_FILE="$STATE_DIR/${PREFIX}.json"
[[ -f "$STATE_FILE" ]] || die "State file not found: $STATE_FILE — run up.sh first"

confirm "Restart production apps for prefix '$PREFIX'?"

# Read all apps.bicep parameters from the state file
IMAGE_TAG=$(                    jq -r '.imageTag'                   "$STATE_FILE")
CAE_ID=$(                       jq -r '.caeId'                      "$STATE_FILE")
CAE_DEFAULT_DOMAIN=$(            jq -r '.caeDefaultDomain'           "$STATE_FILE")
INGEST_IDENTITY_ID=$(            jq -r '.ingestIdentityId'           "$STATE_FILE")
INGEST_CLIENT_ID=$(              jq -r '.ingestClientId'             "$STATE_FILE")
INGEST_PRINCIPAL_ID=$(           jq -r '.ingestPrincipalId'          "$STATE_FILE")
PROCESSING_IDENTITY_ID=$(        jq -r '.processingIdentityId'       "$STATE_FILE")
PROCESSING_CLIENT_ID=$(          jq -r '.processingClientId'         "$STATE_FILE")
PROCESSING_PRINCIPAL_ID=$(       jq -r '.processingPrincipalId'      "$STATE_FILE")
MANAGEMENT_IDENTITY_ID=$(        jq -r '.managementIdentityId'       "$STATE_FILE")
MANAGEMENT_CLIENT_ID=$(          jq -r '.managementClientId'         "$STATE_FILE")
MANAGEMENT_PRINCIPAL_ID=$(       jq -r '.managementPrincipalId'      "$STATE_FILE")
PORTAL_IDENTITY_ID=$(            jq -r '.portalIdentityId'           "$STATE_FILE")
PORTAL_CLIENT_ID=$(              jq -r '.portalClientId'             "$STATE_FILE")
PORTAL_PRINCIPAL_ID=$(           jq -r '.portalPrincipalId'          "$STATE_FILE")
REPORTING_IDENTITY_ID=$(         jq -r '.reportingIdentityId'        "$STATE_FILE")
REPORTING_CLIENT_ID=$(           jq -r '.reportingClientId'          "$STATE_FILE")
REPORTING_PRINCIPAL_ID=$(        jq -r '.reportingPrincipalId'       "$STATE_FILE")
MIGRATOR_IDENTITY_ID=$(          jq -r '.migratorIdentityId'         "$STATE_FILE")
MIGRATOR_CLIENT_ID=$(            jq -r '.migratorClientId'           "$STATE_FILE")
APP_INSIGHTS_CONNECTION_STRING=$( jq -r '.appInsightsConnectionString' "$STATE_FILE")
KV_URI=$(                        jq -r '.kvUri'                     "$STATE_FILE")
ACR_LOGIN_SERVER=$(               jq -r '.acrLoginServer'            "$STATE_FILE")
STORAGE_ACCOUNT_NAME=$(           jq -r '.storageAccountName'        "$STATE_FILE")
CHECKPOINT_BLOB_CONTAINER_URL=$(  jq -r '.checkpointBlobContainerUrl' "$STATE_FILE")
EH_FQDN=$(                       jq -r '.ehFqdn'                    "$STATE_FILE")
CMD_SB_FQDN=$(                    jq -r '.cmdSbFqdn'                 "$STATE_FILE")
SIM_SB_FQDN=$(                    jq -r '.simSbFqdn'                 "$STATE_FILE")
SQL_CONNECTION_STRING=$(           jq -r '.sqlConnectionString'       "$STATE_FILE")
CMD_SB_ISSUER_KEY_SECRET_URI=$(    jq -r '.cmdSbIssuerKeySecretUri'   "$STATE_FILE")
SIM_SB_ISSUER_KEY_SECRET_URI=$(    jq -r '.simSbIssuerKeySecretUri'   "$STATE_FILE")
PORTAL_JWT_KEY_SECRET_URI=$(       jq -r '.portalJwtKeySecretUri'     "$STATE_FILE")
REPORTING_JWT_KEY_SECRET_URI=$(    jq -r '.reportingJwtKeySecretUri'  "$STATE_FILE")
CTRL_CAE_ID=$(                    jq -r '.ctrlCaeId'                 "$STATE_FILE")
CONTROLLER_LOCATION=$(             jq -r '.ctrlLocation'              "$STATE_FILE")
COLLECTOR_PULL_IDENTITY_ID=$(      jq -r '.collectorPullIdentityId'   "$STATE_FILE")

info "Redeploying apps.bicep with tag: $IMAGE_TAG"
BEFORE=$(date +%s)

az deployment group create \
  --resource-group "$(rg_prod)" \
  --name "apps-restart-${PREFIX}" \
  --template-file "$SCRIPT_DIR/../production/apps.bicep" \
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
  --output none

AFTER=$(date +%s)
ELAPSED=$(( AFTER - BEFORE ))
info "Restart complete in ${ELAPSED}s. Record in README as PoC measurement 1."
