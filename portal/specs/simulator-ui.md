# Spec: Simulator — UI (`web/src/app/simulation`) — DEMO SCAFFOLDING

Part of the `portal` module. Module rules, layout and technology: `portal/CLAUDE.md`.

## Visual rules

- Amber hazard-stripe bar and persistent banner "SIMULATOR — demo scaffolding, operates outside the modelled network boundary" on every screen of this area.
- The topology map draws the site/cloud boundary explicitly: a **controller space** zone (customer sites, labelled with its region), a **production space** zone (platform), and the **simulator bus** drawn *outside both zones* with a dashed amber outline and the label "demo channel — not part of the modelled network".
- Customer-facing previews (if any link to them) open in a new tab with the customer theme; never embed customer views here.

Routes: `/simulation` (overview), `/simulation/fleet`, `/simulation/buffers`, `/simulation/scenarios`, lazy-loaded from `simulation/simulation.routes.ts` (`SIMULATION_ROUTES`). The nav shows a separate amber "Simulator" entry, visually set apart from admin entries.

## Data flow

- On entering the area: load `/api/sim/collectors`, `/api/sim/topology`, `/api/sim/metrics` for the last 30 min, `/api/sim/timeline` for the last 30 min, then connect to `/hubs/sim` and apply events to the signal store. Keep a rolling 30 min window in memory (configurable 5/15/30/60 min).
- Reconnect automatically; after reconnect, re-fetch history to fill the gap and show a "reconnected" toast.

## Screens

- **Overview:** topology map (ECharts graph, fixed layout: collectors in the controller zone grouped by region label, then ingest → Event Hubs → processing → SQL → reporting; management and command bus beside ingest; sim bus outside). Node colour by state; every production container app node (ingest, management, processing, portal, reporting) shows its **current number of running replicas** as a prominent badge, and the badge animates when the count changes; the controller zone shows the number of running collector apps; edges animate when active; link-down collectors shown with a broken edge. Beside the map: a **Replicas** panel listing each production container app with its current running replica count and its configured min/max, updated live. Below: live charts — ingest batches/s, Event Hubs lag, running replicas per app (one line per app), processing events/s, dead letters/min, restated minutes — all sharing one time axis with **timeline markers as vertical lines** (ECharts `markLine`) labelled with the action text; hover shows details.
- **Fleet:** grid of registered collectors (tenant, site, region, status, power, provisioning state, link, rate, paused, buffer %, overflow badge). Multi-select plus target selector (selection / site / tenant / region / all). Actions: Power on/off, Set link up/down, Set rate, Pause/Resume, Set buffer capacity, Set invalid share (with kinds). Fleet generator dialog (tenant, sites, lines per site, assets per line, region label, command channel) with a note that it uses the ordinary admin registration path.
- **Buffers:** one card per powered collector, prominent: gauge of depth vs capacity, sparkline over the window, link state, drain rate; a permanent red "OVERFLOWED" badge once `bufferOverflowEver` is true (never cleared in the UI).
- **Scenarios:** list with descriptions and parameter forms; Run; live progress (current step, elapsed, next step); Stop; history table.

