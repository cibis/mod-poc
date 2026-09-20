# Spec: simulated link gate

Part of the `edge` module. Module rules and layout: `edge/CLAUDE.md`.

- `LinkGate.IsUp` (default up). `SetLink down` → all product traffic fails fast: ingest, management API (config, health, renew, command-channel tokens) and the product command channel receiver (close it; reopen when the link returns).
- The simulator channel and `/healthz` do not go through the gate.
