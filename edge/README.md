# edge — simulated edge collector

A .NET 10 worker that behaves like the proposal's edge collector. Runs as a container app in controller space; one instance per collector, created and removed by the portal simulator.

## Departures from a real edge collector

| Area | This PoC | Real deployment |
|---|---|---|
| Sources | Simulated in-process (configurable rate, injected invalids) | PLC/SCADA OPC-UA or Modbus adapters |
| Key storage | Files on the `DATA_DIR` volume | Hardware-backed key store or TPM |
| Buffer | SQLite WAL file — lost if the container is replaced | Persistent durable queue with guaranteed delivery |
| Update rings | Not implemented | Staged rollout via container app revisions |
| Simulator channel | Present (demo scaffolding) | Absent — replaced by real fleet management |
| Product command channel SAS | `ProductCommandListener` uses a namespace-scoped SAS token issued by `ca-mgmt` (`sr=https://fqdn/`) because `ServiceBusClient(fqdn, AzureSasCredential)` validates the token against the namespace URI. The PoC cmd/reply queue names also embed a `~` separator (Azure normalises `/` → `~` at creation). Production should issue per-entity SAS tokens and avoid path separators in queue names. |

## Running locally

1. Copy `.env.example` to `.env.local` and fill in the values for a locally running management API and ingest API.
2. Set `LOCAL_INSECURE_TLS=true` when the platform APIs use a self-signed certificate.
3. Export the variables and run:

```sh
dotnet run --project src/Mod.Collector
```

The health endpoint is available at `http://localhost:8080/healthz`.

## Building

```sh
dotnet build Mod.Edge.slnx
```

## Container image

```sh
az acr build --registry <registry> --image mod/collector:dev --file Dockerfile .
```

Build context is `edge/` (this directory).
