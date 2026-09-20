# Manufacturing Operational Data (MOD) Cloud Platform — Proof of Concept: Build Specification

Version 1.1

## 0. Relationship to the architecture proposal

- This document is an addendum to the architecture proposal *Cloud Platform for Manufacturing Operational Data*, v2.2.
- It specifies only the PoC's goals and its departures from the proposal.
- For everything not covered here, implement as the proposal specifies: collector, ingest API, Event Hubs log, processing, storage, tenancy, security, administration portal, command channel.
- If this document and the proposal conflict, this document wins for the PoC. If this document is silent, the proposal governs.

---

## 1. Goals

The PoC must demonstrate, on real Azure infrastructure:

1. **One-way data flow.** Data travels from sites to the cloud only. Sites accept no inbound connections.
2. **Connectivity loss affects freshness, not data.** During an outage the collector buffer fills; after reconnection the backlog drains and affected rollups restate.
3. **Backlog-driven scaling.** Processing scales on consumer lag, visibly and live.

The PoC must also measure:

- buffer drain behaviour after an outage
- scaling response to backlog
- storage volumes and shapes

The PoC is a demonstration and measurement tool. Do not optimise any part of it for reuse in the product.

---

## 2. Deployment topology

Create two independent deployments, each with its own Bicep entry point. Each must be deployable and destroyable on its own.

| Deployment | Contents |
|---|---|
| **Controller space** | Represents customer sites. Simulated collectors run here as Azure Container Apps. |
| **Production space** | The platform from the proposal, reduced to its demonstrable core, plus the simulation control plane (§3). |

Network rules:

- The two deployments communicate **only over the public internet**.
- No shared VNet, no peering, no Private Link.
- Production space must have no path to initiate a connection into controller space.
- Deploy controller space to a **different Azure region** from production space.

---

## 3. PoC-only additions

The three components below exist only for the demonstration. Label each as PoC/demo scaffolding everywhere it appears: UI, code, repository.

### 3.1 Simulation control plane inside the administration portal

- Serve the simulation dashboard and controls from **the same backend as the MOD administration portal**.
- Use **the same login and the same authorisation model** as the portal.
- Do not create a second application, a second identity system or a separate URL.
- The simulator operates on the **same registry** the portal administers. Tenants, sites and collectors created through the normal portal flow are the ones the simulator can operate.
- Do not register anything twice. Do not introduce a parallel collector model.

### 3.2 On/off toggle provisions registered collectors

- The simulation view lists collectors **already registered through the ordinary administration flow**.
- Provide one action per collector: **On** or **Off**.
  - **On:** provision a container app in controller space, initialised with that collector's identity and configuration. It then enrols, connects and begins producing data through the normal product path.
  - **Off:** stop and remove the container app.
- Collector registration, identity and configuration remain product concerns, implemented as the proposal specifies. The simulator controls only whether a registered collector is running.
- Do not special-case collector trust or enrolment for the PoC.

### 3.3 Separate simulator command channel, immune to simulated outages

Simulation instructions (set rate, drop link, pause, inject bad data, run scenario) travel over the proposal's Service Bus command channel, **on separate queues reserved for the simulator**.

Behaviour:

- **Product channels are subject to the simulated outage.** When a site's link is "down", its data path and its ordinary command queues go silent.
- **Simulator queues are not subject to the simulated outage.** They keep working so the simulator can observe the site and restore the link.

Requirements:

- The simulator channel must be **structurally separate** from product channels: separate queues, separate credentials, separate code path.
- A simulated fault must not be able to silence the simulator channel.
- Structure the code so the two channels cannot become coupled by later changes.
- Label the simulator channel in the UI as demo scaffolding operating outside the modelled network boundary.
- Platform logic must never read from the simulator channel. Derive freshness, completeness and staleness only from what the ingest path actually received, as the proposal requires.

---

## 4. Simulator capabilities

Implement the following capabilities. Screen layout is left to implementation.

**Fleet control**

- Turn registered collectors on and off.
- Change collector behaviour: event rate, link state, pause, buffer size, share of invalid data.
- Apply any change to one collector, a selected group, or all collectors at once.
- Support a whole region losing its link and reconnecting simultaneously. Treat this as the primary demonstration scenario.

**Scenarios**

Provide named scenarios that run unattended:

- steady state
- one site offline
- regional outage
- rate ramp
- bad data
- buffer overflow

**Visualisation**

- A system map showing every node, its state and the flow between nodes, with the site/cloud boundary drawn explicitly.
- Live activity and scaling metrics per component, updating in real time.
- The current number of running replicas of every platform component, shown live and updated as it scales.
- Simulator actions marked on the time axis of all live charts.
- Each collector's buffer shown prominently and live: filling during an outage, draining afterwards. If a buffer ever overflows, mark it permanently.

**Customer-facing views**

- Implement dashboards, restatement markers, and dead-letter/replay in minimal form only.
- Make the boundary between simulator views and customer views visually as obvious as the network boundary on the system map.

---

## 5. Tenancy

- Simulate collectors belonging to **multiple customers**.
- All tenants use shared (pooled) infrastructure, separated by tenant key and row-level security as the proposal specifies.
- **The PoC does not support dedicated (isolated) tenant infrastructure.** Do not implement dedicated data planes, per-tenant databases or resource groups, placement routing, or switching a tenant between pooled and dedicated modes. The administration portal must show the dedicated isolation option in a disabled state, with a visible note stating that dedicated infrastructure is not supported in the PoC.

---

## 6. Environment operation

- **One command** creates the entire environment. **One command** destroys it. Both must be idempotent.
- **Prerequisites:** an Azure subscription and a shell only.
  - No local container tooling (build images in Azure, e.g. ACR Tasks).
  - No Azure portal steps.
  - No manually created identities or pre-existing resources.
- **Inputs:** sensible defaults for everything. A short resource-name prefix is the only expected input.
- **Cost:**
  - Use the cheapest viable SKUs by default.
  - Scale to zero wherever possible.

---

## 7. Out of scope

Do not implement:

- real equipment protocols (OPC UA, EtherNet/IP, S7, Modbus)
- production-grade authentication
- entitlements or metering
- multi-region operation of the platform
- dedicated (isolated) tenant infrastructure
- correctness of manufacturing metrics (OEE etc.)
- cost estimation, cost display, cost measurement or budget alerts

Record every departure from the proposal in the repository and show it in the UI, including:

- reduced security posture
- the simulator command channel
- the simulator's ability to provision collectors

---

## 8. Measurements to record

Do not estimate these; measure them during the build and record the results in the repository as they are settled:

1. Whether the environment can be cold-stopped and restarted, so it can remain deployed for a week between demonstrations.
