# Contract: Dead-letter reason codes and statuses

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

| ReasonCode | Condition |
|---|---|
| BatchUnreadable | Event Hubs body cannot be decompressed or parsed (should not occur) |
| SchemaInvalid | Required field missing or wrong type |
| UnsupportedSchemaVersion | schemaVersion major ≠ 1 |
| UnknownEventType | eventType not in the enum |
| UnmappedSource | No active `AssetSourceMapping` for (collectorId, sourceId) |
| TimestampOutOfRange | eventTime > receivedAt + 5 min, or eventTime < receivedAt − 30 days |
| CounterDecreaseWithoutReset | Counter value lower than the previous value for the same source and name, with `reset` = false |
| ValueOutOfRange | ProcessValue not finite or \|value\| > 1e9 |

Statuses: `New`, `ReplayRequested`, `Replayed`, `Discarded`.

Simulated invalid data (collector `SetInvalidShare`) produces events that pass edge schema validation but fail in the cloud: `UnmappedSource` (sourceId `unmapped-sim`), `TimestampOutOfRange` (eventTime + 1 day), `CounterDecreaseWithoutReset` (value lower than previous, reset false), `ValueOutOfRange` (value 1e12).
