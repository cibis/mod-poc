# Spec: Mod.ManagementApi — collector management endpoint

Part of the `platform` module. Module rules and layout: `platform/CLAUDE.md`.

Everything is pulled by the collector. CA certificate with private key loaded from Key Vault (secret of certificate `CA_CERT_NAME`, PKCS#12) at startup and every 6 h; CSRs parsed and signed with `CertificateRequest`; SAS tokens minted with HMAC-SHA256 in the standard Service Bus format `SharedAccessSignature sr=…&sig=…&se=…&skn=…`.

## Behaviour details

- **Enrolment:** hash the presented token with SHA-256 and look it up in `registry.EnrolmentToken`. Valid means exists, `UsedAt` null, `ExpiresAt` > now, and the collector status is `Registered` or `Enrolled`. Mark the token used and issue the certificate inside one transaction with `UPDLOCK` on the token row so a token can never be used twice. Constant-time comparison is not required because the lookup is by hash, but always return the same 401 body for every token failure.
- **CSR checks:** PEM parses, signature valid, key is EC P-256 (reject others with 400 `CsrInvalid`).
- **Config document:** built from `registry.Collector`, `registry.CollectorConfig` (settings JSON merged over defaults), active `registry.AssetSourceMapping` rows joined with `registry.Asset`, plus `INGEST_URL` and `MANAGEMENT_URL`. `configVersion` = `CollectorConfig.Version`.
- **Health:** validate the body, then write the tables listed under POST /v1/health in `contracts/management-api.md` in one transaction. Reject a report whose `collectorId` differs from the certificate (400 `CollectorMismatch`). Accept `minuteCounts` only for minutes in the past and at most 7 days old.
- **Command channel:** `enabled` = `Collector.CommandChannelEnabled`. Queue names from `contracts/command-channel.md` using the collector's TenantId. Tokens are minted only for this collector's two queues.
- **Audit:** write `registry.AuditLog` for `CollectorEnrolled`, `CertificateRenewed`, `EnrolmentRejected` (ActorKind `Collector`).
- **Housekeeping:** hourly delete `CollectorHealthHistory` older than 7 days.

