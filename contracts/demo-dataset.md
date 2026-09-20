# Contract: Demo dataset (pre-configured at creation)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

## Goal

Immediately after `infra/scripts/up.sh` completes, an administrator can log in, open the simulator and power on collectors — no tenants, sites, assets, collectors, mappings or users need to be created by hand. Customer users can log in and see their (initially empty, then filling) dashboards. All seeded data is created through the database seed and completed by the portal's reconcilers; it is ordinary product data, not special-cased.

## Seeded content

All tenants are `Pooled`, status `Active`. All sites use time zone `Europe/Berlin`.

| Tenant (slug) | Site | RegionLabel | Lines | Collectors |
|---|---|---|---|---|
| Customer A (`customer-a`) | Customer A North 1 | EU-North | 2 | 1 (site) |
| | Customer A North 2 | EU-North | 2 | 1 (site) |
| | Customer A South 1 | EU-South | 2 | 1 (site) |
| Customer B (`customer-b`) | Customer B North 1 | EU-North | 2 | 1 (site) |
| | Customer B South 1 | EU-South | 2 | 1 (site) |
| Customer C (`customer-c`) | Customer C North 1 | EU-North | 2 | 1 (site) |
| | Customer C South 1 | EU-South | 2 | 2 (one per line) |

Totals: 3 tenants, 7 sites, 14 lines, 56 assets, 8 collectors (4 in EU-North, 4 in EU-South).

Per line: 4 assets, names `{Line name} {AssetType}`, `AssetType` in order Filler, Capper, Labeler, Packer; `CounterName` `good_count`; `ProcessValueName` `speed`, unit `units/min`, min 0, max 600. Line names `Line 1`, `Line 2`.

Per collector:
- Name: `{Site name} Collector` (site collectors) or `{Site name} Line {n} Collector` (Customer C South 1).
- Status `Registered`, `CommandChannelEnabled` = 1.
- `CollectorConfig` version 1 with the ConfigDocument defaults (`contracts/management-api.md`).
- One active mapping per asset it serves, sourceId `L{lineIndex}-A{assetIndex}` (1-based within the site). Customer C South 1: the Line 1 collector maps only `L1-*`, the Line 2 collector only `L2-*`.
- A `sim.CollectorState` row with `PoweredOn` = 0, `ProvisioningState` = `Off`, `BufferOverflowEver` = 0.

Users (in `registry.AppUser`):

| UserName | Kind | Tenant | Password |
|---|---|---|---|
| `admin` | ModAdmin | — | Key Vault secret `admin-password` |
| `customer-a-viewer` | Customer | Customer A | Key Vault secret `customer-password` |
| `customer-b-viewer` | Customer | Customer B | Key Vault secret `customer-password` |
| `customer-c-viewer` | Customer | Customer C | Key Vault secret `customer-password` |

## Identity and idempotency

- IDs are deterministic: UUID v5 in namespace `6f2c1a4e-9b1d-4c1e-8a57-3d0e2b7c9a10` over the path of natural names, e.g. `tenant:customer-a`, `site:customer-a/Customer A North 1`, `line:…/Line 1`, `asset:…/Line 1/Filler`, `collector:customer-c/Customer C South 1/Line 2`. Lowercase GUID strings.
- The seed inserts only rows that are missing (by primary key). It never updates or deletes existing rows, so administrator changes survive re-runs of `up.sh`, and deleted seed rows are recreated.
- Controlled by `SEED_DEMO_DATA` (default `true`).

## Completion by the portal (not by the seed)

- **Product command queues** for seeded collectors are created by the portal's queue reconciler, which runs at startup and every 60 s (the seed may run after the portal has started).
- **Enrolment tokens** are not seeded. The simulator issues one at power On through the ordinary enrolment-token service.

## Scenario defaults tied to this dataset

| Scenario | Default parameters |
|---|---|
| steady-state | All registered collectors |
| one-site-offline | `Customer B North 1 Collector` |
| regional-outage | `regionLabel` = `EU-North` (4 collectors across all three tenants) |
| buffer-overflow | `Customer A South 1 Collector` |
| bad-data | `share` = 0.1, all powered collectors |

## Ready-to-demo state (what the operator sees)

1. `up.sh` prints the portal URL, the customer URL, the user names above and the commands to read the two passwords.
2. Admin portal → Simulator → Fleet lists the 8 collectors as Off, grouped by region.
3. "Power on all" (or the `steady-state` scenario) starts them; each enrols and begins sending within about a minute.
4. Each `*-viewer` sees only their tenant's sites filling in.
