# Contract: Simulator channel (Service Bus) — DEMO SCAFFOLDING

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

This channel exists only for the demonstration and operates outside the modelled network boundary. Platform components (ingest-api, processing, management-api, reporting-api) never read it, and have no credentials for it.

- Separate Service Bus namespace (Basic tier), FQDN in `SIM_SB_FQDN`. Separate namespace, separate credentials and separate code path from the product command channel.
- Queues:
  - `sim/{collectorId}` — commands, portal-api → collector. Created at power On, deleted at power Off. TTL 60 s.
  - `sim-status` — status, all collectors → portal-api. Created by infra. TTL 30 s.
- Credentials: namespace SAS rule `sim-issuer` (Listen, Send). At power On, portal-api mints a Listen token for `sim/{collectorId}` and a Send token for `sim-status`, both valid 30 days, and passes them to the collector container as secrets (`SIM_COMMAND_SAS`, `SIM_STATUS_SAS`). Never issued through management-api.
- **Command** (portal-api → collector): `MessageId` = commandId. Body: `commandId` GUID, `type`, `issuedAt`, `args`. No reply message; the effect appears in the next status.

| Type | Args |
|---|---|
| SetRate | `eventsPerSecondPerSource` number 0.01–50 |
| SetLink | `state` `"up"` \| `"down"` |
| SetPaused | `paused` bool. When true, sources generate no events (machine stopped). Forwarding, health reporting and buffer draining continue |
| SetBufferCapacity | `capacityEvents` int 100–1,000,000. Overrides the config value until the container restarts |
| SetInvalidShare | `share` number 0–1; `kinds` optional array of `UnmappedSource`, `TimestampOutOfRange`, `CounterDecreaseWithoutReset`, `ValueOutOfRange` (default: all) |

- **Status** (collector → portal-api): sent every 2 s and immediately after applying a command. Application property `mod.collectorId`. Body:
  `collectorId`, `sentAt`, `softwareVersion`, `enrolled` bool, `authRejected` bool, `linkState`, `paused`, `eventsPerSecondPerSource`, `invalidShare`, `bufferDepthEvents`, `bufferCapacityEvents`, `bufferOverflowEver` bool (sticky), `overflowDroppedTotal`, `producedTotal`, `ackedTotal`, `lastAckAt`, `lastAppliedCommandId`.
- **Link semantics:** `down` makes every product-bound connection (ingest, management, product command channel) fail as if the network were unreachable. The simulator channel is unaffected by link state.
