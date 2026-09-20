# Internal contract: Administration HTTP API (API ↔ admin UI)

Part of the `portal` module. Module rules, layout and technology: `portal/CLAUDE.md`.

Same origin as the admin-portal SPA. All routes except login require a ModAdmin JWT. Errors use `application/problem+json` with `type` = code. Every mutating call writes `registry.AuditLog`. Lists support `?page=1&pageSize=50` and return `{items, total}`.

| Method and route | Purpose / body → response |
|---|---|
| POST /api/auth/login | See `contracts/browser-auth.md` |
| GET /api/admin/tenants | `[{tenantId, name, slug, isolationMode, status, siteCount, collectorCount}]` |
| POST /api/admin/tenants | `{name, isolationMode}` → tenant. `isolationMode` other than `Pooled` → 400 `DedicatedNotSupportedInPoc` |
| GET /api/admin/tenants/{tenantId}/hierarchy | `{tenant, sites:[{…site, lines:[{…line, assets:[…]}], collectors:[…]}]}` |
| POST /api/admin/tenants/{tenantId}/sites | `{name, regionLabel, timeZone}` → site |
| POST /api/admin/sites/{siteId}/lines | `{name}` → line |
| POST /api/admin/lines/{lineId}/assets | `{name, assetType, processValueName, processValueUnit, processValueMin, processValueMax}` → asset |
| GET /api/admin/collectors?tenantId&siteId&status | Fleet list: `[{collectorId, name, tenantId, tenantName, siteId, siteName, regionLabel, status, lastSeenAt, softwareVersion, reportedConfigVersion, configVersion, certificateExpiresAt, bufferDepthEvents, bufferCapacityEvents, freshnessAgeSeconds}]` |
| POST /api/admin/sites/{siteId}/collectors | `{name, commandChannelEnabled}` → collector. Creates collector (`Registered`), config version 1 with defaults, one active mapping per asset of the site (sourceId `L{lineIndex}-A{assetIndex}`, 1-based by creation order), product command queues if enabled |
| GET /api/admin/collectors/{id} | Detail: collector, config (version + settings), mappings, certificates, latest health, reconciliation for the last 60 minutes |
| PUT /api/admin/collectors/{id}/config | `{batching, forwarder, buffer, intervals, commandChannelEnabled}` → new version |
| PUT /api/admin/collectors/{id}/mappings | `[{sourceId, assetId}]` → closes changed mappings (ValidTo = now), opens new ones, bumps config version |
| POST /api/admin/collectors/{id}/enrolment-tokens | → `{token, expiresAt}`. Token shown once; stored hashed; 24 h; single use |
| POST /api/admin/collectors/{id}/revoke | `{reason}` → revokes all certificates, Status `Revoked`, deletes product queues |
| POST /api/admin/collectors/{id}/commands | `{type}` (GetStats, GetDiagnostics, ReloadConfig, Restart) → `{requestId, outcome, result, error, elapsedMs}`; waits up to the type's deadline |
| GET /api/admin/collectors/{id}/health-history?from&to | `[{receivedAt, bufferDepthEvents, bufferCapacityEvents, overflowDroppedTotal}]` |
| GET /api/admin/dead-letters?tenantId&collectorId&reasonCode&status | Paged list |
| GET /api/admin/dead-letters/{id} | Detail including EventJson |
| POST /api/admin/dead-letters/replay | `{ids}` or `{filter:{tenantId, collectorId, reasonCode}}` → `{updated}`; sets Status `ReplayRequested` |
| POST /api/admin/dead-letters/discard | `{ids}` → `{updated}` |
| GET /api/admin/restatements?tenantId&from&to | Restated rollup minutes with asset, line, site names |
| GET /api/admin/audit?tenantId&from&to&action | Paged audit entries |
