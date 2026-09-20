# Spec: Administration — API behaviour (`Features/Admin`)

Part of the `portal` module. Module rules, layout and technology: `portal/CLAUDE.md`.

## Behaviour details

- **Tenant creation:** `isolationMode` must be `Pooled`; anything else → 400 `DedicatedNotSupportedInPoc`. The UI shows the Dedicated option disabled; the API enforces it.
- **Collector registration:** Status `Registered`; `CollectorConfig` v1 with ConfigDocument defaults; mappings for every asset of the site with sourceIds `L{lineIndex}-A{assetIndex}`; if `commandChannelEnabled`, create both product queues (`contracts/command-channel.md`). All in one SQL transaction; queue creation after commit, retried; a queue reconciler (at startup and every 60 s) creates any missing queues for collectors with the flag set and deletes queues of revoked or retired collectors. This is what completes the seeded collectors of `contracts/demo-dataset.md`, whose seed may run after the portal starts.
- **Config and mapping changes:** increment `CollectorConfig.Version` in the same transaction.
- **Revoke:** set `RevokedAt` on all active certificates, Status `Revoked`, delete product queues, audit.
- **Command dispatcher:** send the request message; a single background receiver per reply queue in use (created on demand, closed after 2 min idle) completes pending requests by `CorrelationId`; unknown or late replies are completed and dropped. Deadline and outcome rules from `contracts/command-channel.md`.
- **Dead letters:** only status changes (`ReplayRequested`, `Discarded`) — processing performs the replay.
- **Audit:** every mutating endpoint writes one entry.
