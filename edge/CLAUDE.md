# Module: edge — simulated edge collector

A .NET collector that behaves like the proposal's edge collector, except that its sources are simulated instead of reading PLCs. It runs as one container app per collector in **controller space** (a different Azure region), created and removed by the simulator in the portal module.

Its counterparts are the `platform` module (ingest and management APIs, product command channel tokens) and the simulator in the `portal` module (simulator channel, container provisioning). Everything needed about them is in the shared contracts listed in the task table. It enrols with a one-time token, pulls configuration, generates canonical events, buffers durably, forwards batches outbound, reports health, optionally answers product commands, and obeys the simulator channel.

It never opens a listening port except `/healthz` for container probes (no ingress is configured).

## How to work in this module

This file is deliberately short. **Read only the spec and contract files listed for the task you are doing**, and nothing else outside this module. One task per session; `/clear` between tasks.

| Task | Spec files (this module) | Shared files (under `/contracts/` unless shown otherwise) |
|---|---|---|
| Data path: simulated sources, edge validation, buffer, forwarder | `specs/data-path.md`, `specs/link-gate.md` | `canonical-event.md`, `ingest-api.md`, `dead-letter-reason-codes.md` (simulated invalid kinds) |
| Control: enrolment and identity, config, health, certificate renewal, product command channel, simulator channel | `specs/control.md`, `specs/link-gate.md` | `management-api.md`, `collector-certificates.md`, `command-channel.md`, `simulator-channel.md`, `environment/collector.md` |
| Host, Dockerfile, solution, README | this file | `environment/collector.md`, `images.md` |

Recommended order: host and buffer → data path → control.

## Technology

- .NET 10 worker service (`Microsoft.Extensions.Hosting`), single process, several `BackgroundService`s.
- `Microsoft.Data.Sqlite` for the durable buffer (WAL mode, `synchronous=NORMAL`), in `DATA_DIR`.
- `HttpClient` via `SocketsHttpHandler` with the client certificate attached; gzip request bodies.
- `Azure.Messaging.ServiceBus` with `AzureSasCredential` for both the product command channel and the simulator channel.
- `System.Security.Cryptography` for key generation and CSR (`CertificateRequest.CreateSigningRequestPem`).

## Layout

```
edge/
  CLAUDE.md
  specs/                            data-path.md, control.md, link-gate.md
  Dockerfile                          context = edge/
  Mod.Edge.slnx
  src/Mod.Collector/
    Program.cs
    Identity/IdentityStore.cs          key pair, certificate, renewal
    Identity/EnrolmentClient.cs
    Config/ConfigService.cs            pull, apply atomically, last-known-good
    Sources/SimulatedSource.cs         one per configured source
    Sources/InvalidDataInjector.cs
    Edge/CanonicalMapper.cs, EdgeValidator.cs
    Buffer/SqliteBuffer.cs             append, peek batch, delete, capacity, overflow policy
    Forwarding/Forwarder.cs            batching, rate limit, retry with jitter
    Health/HealthReporter.cs, MinuteCounter.cs
    Network/LinkGate.cs                simulated link state (all product traffic goes through it)
    Commands/ProductCommandListener.cs
    Simulation/SimChannelListener.cs, SimStatusPublisher.cs   DEMO SCAFFOLDING
  README.md
```

## Done when

- `dotnet build` passes on `Mod.Edge.slnx`; the collector runs locally against local instances of the platform ingest and management APIs when env vars point to them (development switch `LOCAL_INSECURE_TLS=true` permits a self-signed server cert).
- README lists departures: simulated sources instead of PLCs, file-based key, ephemeral buffer, no update rings, simulator channel.
- `.env.example` committed at the module root, listing every variable of `contracts/environment/collector.md` with empty values. No secret in any tracked file.
