# MOD PoC — notes for people (not loaded by Claude Code)

## What this repository is

A proof of concept of the architecture in `docs/MOD_Cloud_Platform_Architecture_Proposal.pdf`, scoped by `docs/MOD_PoC_Scope.md`. It demonstrates on real Azure infrastructure:

1. One-way data flow from simulated customer sites that accept no inbound connections.
2. Connectivity loss affects freshness, not data: buffers fill, drain and rollups restate.
3. Processing scales on backlog, visibly and live.

It is also used to measure drain behaviour, scaling response and storage shapes. Nothing here is product code.

A freshly created environment is ready to demo: three customers with sites, assets, collectors and users are pre-configured (`contracts/demo-dataset.md`), so collectors can be powered on straight away.

## Recorded departures from the proposal

Each module README lists the departures it implements. The full list:

- Local user accounts with generated passwords instead of Entra External ID / customer federation.
- No Front Door or WAF; container app ingress endpoints are public.
- No private endpoints or VNet integration; SQL allows Azure services.
- PoC CA key stored exportable in Key Vault; collector private key in a file, not a TPM.
- Product command requests are not signed; diagnostics are returned inline, not uploaded to blob.
- Raw events stored in Azure SQL clustered columnstore (the proposal defers ADX vs SQL).
- No cold archive to ADLS, no exports, no Azure SignalR Service (in-process SignalR, single replica).
- No collector software update rings.
- Collectors run as container apps with an ephemeral `EmptyDir` buffer (survives process restarts, not replica replacement).
- Dedicated tenant infrastructure is not supported; the option is shown disabled in the portal.
- Simulator channel and simulator-driven provisioning (demo scaffolding).
- No cost estimation, display, measurement or budget alert in the PoC.

## Suggested build order

1. `platform` (database first, then ingest-api, management-api, processing)
2. `edge`
3. `portal` (Admin part first, then the Simulator part)
4. `customer`
5. `infra`

Full step-by-step commands and prompts: `BUILD.md` in the repository root. Compile-and-run instructions for developers: `SETUP.md`. Run one task per Claude Code session and `/clear` between tasks.
