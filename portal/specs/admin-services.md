# Spec: Admin service interfaces (API)

Part of the `portal` module. Module rules, layout and technology: `portal/CLAUDE.md`.

Used by the Admin feature (implements them) and the Simulation feature (consumes only these).

| Interface | Operations |
|---|---|
| `ITenantService` | List tenants; create tenant (Pooled only); get hierarchy |
| `ISiteService` | Create site, line, asset |
| `ICollectorService` | Register collector for a site (creates config v1, default mappings, product queues when enabled); get; list with filter (tenantId, siteId, regionLabel, status); update config; update mappings; revoke |
| `IEnrolmentTokenService` | Issue token for a collector (returns plaintext once, stores hash, 24 h, audits with actor) |
| `ICertificateService` | Revoke all active certificates of a collector with a reason, **without** changing collector status (used by simulator power-off) |
| `ICommandDispatcher` | Send a product command and await the outcome |
| `IAuditWriter` | Write an audit entry (actor name, actor kind, action, target, tenant, details) |

Each method takes an `Actor` value (name + kind) so audit entries record who acted, including `Simulator`.
