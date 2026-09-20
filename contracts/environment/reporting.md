# Contract: Environment variables — `ca-reporting` (customer)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Set by `infra` (Bicep) for production apps; set by the portal simulator for collector apps.

Also applies: `common.md` (production-space .NET services only; not collectors).

| Variable | Example / default | Notes |
|---|---|---|
| REPORTING_JWT_KEY | secret | From Key Vault secret `reporting-jwt-key` |
| FRESHNESS_STALE_SECONDS | 60 | |
| LIVE_POLL_SECONDS | 2 | Rollup change polling for the hub |
