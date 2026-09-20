# Internal contract: customer reporting HTTP API and live hub (API ↔ customer UI)

Part of the `customer` module. Module rules, layout and technology: `customer/CLAUDE.md`.

Same origin as the customer-portal SPA. All routes except login require a Customer JWT. The tenant comes only from the token's `tenant_id`; never from a route, query or body. Any asset, line or site not belonging to the caller's tenant → 404 (never 403). Errors use `application/problem+json`.

| Method and route | Response |
|---|---|
| POST /api/auth/login | See `contracts/browser-auth.md` |
| GET /api/me | `{userId, displayName, tenantId, tenantName}` |
| GET /api/hierarchy | `{tenantId, tenantName, sites:[{siteId, name, regionLabel, timeZone, lines:[{lineId, name, assets:[{assetId, name, assetType, counterName, processValueName, processValueUnit}]}]}]}` |
| GET /api/sites/status | `[{siteId, name, freshness:{lastEventTime, ageSeconds, status}, completenessLastHour:{produced, accepted, deadLettered, ratio}, openGapCount}]`. `status`: `Current` (age ≤ `FRESHNESS_STALE_SECONDS`, default 60), `Stale`, `NoData` |
| GET /api/sites/{siteId}/summary?from&to | Per-line totals aggregated at query time from asset rollups: `[{lineId, name, eventCount, counterDelta, faultEventCount, restatedMinutes}]` |
| GET /api/assets/{assetId}/rollups?from&to | `[{minuteStart, eventCount, counterDelta, faultEventCount, lastState, processValueAvg, processValueMin, processValueMax, isRestated, restatementCount, lastComputedAt}]` ordered by minuteStart |

`from`/`to` are ISO timestamps; default last 60 minutes; maximum range 24 h (400 `RangeTooLarge`).

**SignalR hub `/hubs/live`** (server → client only):
- On connect the server adds the connection to group `tenant:{tenantId}` taken from the token. Clients cannot choose groups.
- `rollupsUpdated` `{items:[same shape as the rollups endpoint plus assetId]}` — sent when rollups for the tenant were written or restated since the last push.
- `freshnessUpdated` `{sites:[same shape as /api/sites/status]}` — every 5 s.
