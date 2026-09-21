# Contract: Management HTTP API (served by management-api, called by collector)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Base URL: collector env `MANAGEMENT_URL`, also `managementUrl` in the config document. Ingress client-certificate mode `accept`. Every endpoint except `POST /v1/enrol` requires a valid collector certificate (certificate contract); otherwise 401 or 403. Errors are `application/problem+json` with `type` = code.

**POST /v1/enrol** (no certificate)
- Request: `enrolmentToken` string, `csrPem` string, `softwareVersion` string.
- 200: `collectorId`, `tenantId`, `siteId`, `certificatePem`, `caCertificatePem`, `certificateExpiresAt`, `config` (ConfigDocument).
- 400 `CsrInvalid`. 401 `EnrolmentTokenInvalid` (unknown, expired or already used — identical response for all three). 409 `CollectorNotEnrollable` (collector status `Revoked` or `Retired`).
- Effects: token marked used; new certificate row; all earlier non-revoked certificates of this collector get `RevokedAt` = now; collector `Status` = `Enrolled`, `EnrolledAt` = now; audit entry `CollectorEnrolled`.

**GET /v1/config**
- Optional header `If-None-Match: "{configVersion}"`.
- 200 ConfigDocument with `ETag: "{configVersion}"`, or 304 if unchanged.

**POST /v1/health**
- Body: HealthReport. Response 204.
- Effects: upsert `registry.CollectorHealth`; insert `registry.CollectorHealthHistory`; upsert `telemetry.Reconciliation` `ProducedCount` and `DroppedAtEdgeCount` for each reported minute (overwrite, so resends are idempotent); update collector `LastSeenAt`, `SoftwareVersion`, `ReportedConfigVersion`, `ReportedConfigHash`.

**POST /v1/certificate/renew**
- Request: `csrPem`. 200: `certificatePem`, `certificateExpiresAt`.
- Effects: new certificate row; previous certificate gets `SupersededAt` = now but stays valid until expiry so in-flight requests succeed.

**GET /v1/command-channel**
- 200 `{enabled: false}` or `{enabled: true, namespaceFqdn, requestQueue, replyQueue, keyName, key, expiresAt}`.
- `keyName` is the SAS rule name (`collector-issuer`); `key` is its base64-encoded primary key. The collector constructs an `AzureNamedKeyCredential(keyName, key)` and opens a `ServiceBusClient(namespaceFqdn, credential)`. `expiresAt` is 1 h from now; the collector refetches when less than 10 min remain (to handle key rotation).

**ConfigDocument**

| Field | Type | Default / rule |
|---|---|---|
| configVersion | int | `registry.CollectorConfig.Version` |
| collectorId, tenantId, siteId | GUID | |
| ingestUrl | string | |
| managementUrl | string | |
| sources | Source[] | From active `AssetSourceMapping` rows (ValidTo null) joined with `Asset` |
| batching | object | `maxEvents` 500, `maxBytes` 262144, `flushIntervalMs` 2000 |
| forwarder | object | `maxBatchesPerSecond` 5, `retryBaseMs` 1000, `retryMaxMs` 60000 |
| buffer | object | `capacityEvents` 200000 |
| intervals | object | `configPollSeconds` 30, `healthReportSeconds` 30 |
| commandChannelEnabled | bool | |

Source: `sourceId`, `assetName`, `assetType`, `counterName`, `processValue` {`name`, `unit`, `min`, `max`}.

**HealthReport**

| Field | Type | Rule |
|---|---|---|
| collectorId | GUID | |
| reportedAt | timestamp | |
| softwareVersion | string | |
| configVersion | int | Version currently applied |
| configHash | string | SHA-256 hex of the applied config document serialised with sorted keys, no whitespace |
| bufferDepthEvents, bufferCapacityEvents | int | |
| oldestBufferedEventTime | timestamp \| null | |
| overflowDroppedTotal, rejectedBatchesTotal, invalidAtEdgeTotal | int64 | Since collector start |
| clockOffsetMs | int | PoC: 0 |
| diskFreeBytes | int64 | Free space in DATA_DIR |
| sources | array | `{sourceId, lastReadAt, lastSequence}` |
| minuteCounts | array | `{minuteStart, produced, droppedAtEdge}` per eventTime minute; only completed minutes; every minute not yet acknowledged by a 204; max 10,080 entries (oldest dropped first) |
