# Spec: Administration — UI (`web/src/app/features`)

Part of the `portal` module. Module rules, layout and technology: `portal/CLAUDE.md`.

## Routes

`/login`, `/tenants`, `/tenants/:tenantId`, `/collectors`, `/collectors/:collectorId`, `/dead-letters`, `/dead-letters/:id`, `/restatements`, `/audit`, and `/simulation/**` lazy-loaded from `./simulation/simulation.routes` (export `SIMULATION_ROUTES`). All but `/login` require an admin token. The nav shows a separate, amber-styled "Simulator" entry, visually set apart from admin entries.

## Screens and behaviour

- **Tenants:** table; create dialog with name and isolation mode radio group — `Pooled` (selected) and `Dedicated` **disabled**, with an inline note "Dedicated infrastructure is not supported in the PoC." Hierarchy editor: tree of sites → lines → assets with add actions; site form includes `regionLabel`.
- **Collectors (fleet):** filterable table (tenant, site, region, status, certificate expiry) with freshness age, buffer utilisation bar, version, config version (desired vs reported; highlight drift). "Register collector" from a site.
- **Collector detail:** identity and status, certificates (issued, expires, superseded, revoked), latest health, buffer history chart (health-history endpoint), reconciliation last 60 min (produced vs accepted vs dead-lettered), config editor (form for batching, forwarder, buffer, intervals, command channel toggle), mappings editor, "Generate enrolment token" dialog (shows the token once with copy button and expiry), command panel (GetStats, GetDiagnostics, ReloadConfig, Restart with confirm) showing the outcome and result JSON, revoke dialog with reason.
- **Dead letters:** filters (tenant, collector, reason code, status), multi-select replay/discard, bulk "replay all matching filter", detail view with formatted EventJson.
- **Restatements:** table of restated minutes with asset, line, site, count, last computed.
- **Audit:** table with filters.

