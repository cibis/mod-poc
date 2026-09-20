# Contract: Browser authentication (PoC local accounts)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

PoC departure: local accounts in `registry.AppUser` instead of Entra External ID. Passwords are generated at deployment and stored in Key Vault.

- `POST /api/auth/login` request `{userName, password}` → 200 `{accessToken, expiresAt, displayName, kind, tenantId, tenantName}` (tenant fields null for admins); 401 `InvalidCredentials` for any failure.
- Password check: ASP.NET Core Identity `PasswordHasher` v3.
- Access token: JWT, HS256, lifetime 8 h, no refresh tokens. Claims: `sub` = UserId, `name` = DisplayName, `kind` = `ModAdmin` \| `Customer`, `tenant_id` (customers only).
- portal-api accepts only `kind = ModAdmin`; issuer and audience `mod-poc-portal`; key `PORTAL_JWT_KEY`.
- reporting-api accepts only `kind = Customer`; issuer and audience `mod-poc-reporting`; key `REPORTING_JWT_KEY`.
- The browser sends `Authorization: Bearer {token}`. SignalR connections pass the token as the `access_token` query parameter.
- Tokens are held in `sessionStorage` by the SPA (PoC).
