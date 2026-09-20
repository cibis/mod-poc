# Spec: customer UI

Part of the `customer` module. Module rules, layout and technology: `customer/CLAUDE.md`.

Routes: `/login`, `/sites`, `/sites/:siteId`, `/assets/:assetId`.

- **Sites:** list with freshness badge, completeness last hour, open gaps.
- **Site:** lines and assets, line totals for the selected range.
- **Asset:** minute chart (counter delta bars + process value line), state strip, restated minutes highlighted.

## Behaviour

- Banner "Customer view (minimal PoC)" and the tenant name in the header.
- Freshness badge: `Current` (green), `Stale` (amber, with "data delayed since …"), `NoData` (grey). A stale site never shows its last number without the label.
- Restated minutes are shaded and marked with a small "restated" indicator and tooltip "Updated after the period closed (late data)".
- Range picker: last 15 min, 1 h, 6 h, 24 h.
- Live updates from `/hubs/live` merge into the current view (`rollupsUpdated`, `freshnessUpdated`).
- A 404 from the API shows "Not found" — never mention other tenants.

