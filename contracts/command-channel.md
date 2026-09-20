# Contract: Product command channel (Service Bus)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

- Product command namespace (Basic tier), FQDN in `CMD_SB_FQDN`. Completely separate from the simulator namespace.
- Per collector, created by portal-api when `CommandChannelEnabled` becomes true (at registration or later) and deleted when it becomes false or the collector is revoked or retired:
  - request queue `cmd/{tenantId}/{collectorId}`
  - reply queue `reply/{tenantId}/{collectorId}`
  - default message TTL 5 min, lock duration 30 s, max delivery count 3, dead-lettering on expiry off.
- Authorisation: namespace SAS rule `collector-issuer` (Listen, Send), key used only by management-api to mint entity-scoped tokens. Portal-api uses its managed identity (Azure Service Bus Data Owner).
- **Request** (portal-api → collector): `MessageId` = `CorrelationId` = requestId; `ReplyTo` = reply queue name; `TimeToLive` per type; content type `application/json`; application property `mod.type`. Body: `requestId` GUID, `type`, `issuedAt`, `expiresAt`, `args` object.
- **Reply** (collector → portal-api): `CorrelationId` = requestId. Body: `requestId`, `status` (`Succeeded` \| `Failed` \| `Rejected`), `completedAt`, `result` object or null, `error` string or null.
- **Allow-listed types** (anything else → reply `Rejected`):

| Type | TTL / dispatcher deadline | Result |
|---|---|---|
| GetStats | 60 s / 20 s | `bufferDepthEvents`, `bufferCapacityEvents`, `oldestBufferedEventTime`, `forwardRateEventsPerSecond`, `lastAckAt`, `sources[{sourceId,lastReadAt,lastSequence}]` |
| GetDiagnostics | 60 s / 30 s | `softwareVersion`, `configVersion`, `uptimeSeconds`, `recentLogLines` (≤ 200 strings). PoC: inline, no blob upload |
| ReloadConfig | 60 s / 20 s | `configVersion` after an immediate config poll |
| Restart | 60 s / 20 s | Reply `Succeeded` first, then the collector exits with code 0 and Container Apps restarts it |

- The collector completes (without replying) any message whose `expiresAt` has passed. It stores the last 1,000 executed requestIds with their replies under `DATA_DIR`; a redelivered known requestId gets the stored reply resent without re-execution.
- The collector receives on this channel only while its simulated link is up.
- Request signing is not implemented in the PoC (recorded departure).
- Dispatcher outcomes returned by portal-api: `Succeeded`, `Failed`, `Rejected` (from the reply), `TimedOut` (no reply before deadline), `Unreachable` (timed out and the collector's `LastSeenAt` is older than 2 × its health interval).
