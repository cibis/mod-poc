# Spec: reporting API behaviour

Part of the `customer` module. Module rules, layout and technology: `customer/CLAUDE.md`.

## Behaviour details

- `ITenantContext` is resolved once per request from the `tenant_id` claim and passed explicitly; no static state.
- Site status: freshness from max `CollectorFreshness.LastEventTime` over the site's collectors; completeness from `Reconciliation` for completed minutes in the last hour; `openGapCount` from `SequenceGap` with `FilledAt` null.
- Hierarchy aggregation is done at query time from asset rollups (the proposal's "materialise at the leaf, aggregate upward").
- Live push: `RollupChangePoller` runs every `LIVE_POLL_SECONDS`. For each tenant that currently has connections, it opens a connection with that tenant's session context, reads rollups with `LastComputedAt` > the tenant's watermark, and sends `rollupsUpdated` to group `tenant:{tenantId}`. Every 5 s it sends `freshnessUpdated`.
- Group membership is decided by the server from the token on connect; the hub exposes no client-callable methods.

## Database notes

`registry.Collector` is not granted to `role_reporting` — derive a site's collectors from `telemetry.CollectorFreshness.SiteId`. `registry.AppUser` is read for login only (Kind = Customer) and is not covered by RLS. No access to schema `sim`.
