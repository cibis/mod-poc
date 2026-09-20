# Contract: Environment variables — `ca-mgmt` (platform: Mod.ManagementApi)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Set by `infra` (Bicep) for production apps; set by the portal simulator for collector apps.

Also applies: `common.md` (production-space .NET services only; not collectors).

| Variable | Example / default | Notes |
|---|---|---|
| KEYVAULT_URI | `https://kv-modpoc-x.vault.azure.net/` | |
| CA_CERT_NAME | `collector-ca` | Key Vault certificate (read via its secret, includes private key) |
| CERT_VALIDITY_DAYS | 7 | |
| CMD_SB_FQDN | | Product command namespace |
| CMD_SB_SAS_KEY_NAME | `collector-issuer` | |
| CMD_SB_SAS_KEY | secret | Container app secret from Key Vault secret `cmd-sb-issuer-key` |
| INGEST_URL | `https://ca-ingest.<env-domain>` | Put into config documents |
| MANAGEMENT_URL | `https://ca-mgmt.<env-domain>` | Put into config documents |
| COMMAND_TOKEN_LIFETIME_MINUTES | 60 | |
