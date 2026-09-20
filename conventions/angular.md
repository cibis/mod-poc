# Conventions: Angular (portal and customer)

> Shared convention. Read when your task touches `web/`. Do not change without the user's approval.

- Latest stable Angular (v20 or later): standalone components, signals, built-in control flow (`@if`, `@for`), `inject()`, functional guards and interceptors, zoneless change detection, lazy-loaded routes.
- Angular Material (Material 3) + CDK. Apache ECharts via `ngx-echarts` for charts (and graphs). `@microsoft/signalr` for live hubs.
- State: signal-based services per feature (no NgRx).
- ESLint (`angular-eslint`) and Prettier. Node 22 LTS.
- Production builds use relative URLs (`/api/...`, `/hubs/...`): the SPA is served by the module's own API from the same origin.
- Tokens in `sessionStorage` (PoC). Functional interceptor adds `Authorization: Bearer`; 401 → redirect to `/login`.
- Timestamps in local time with UTC on hover; ages as "12 s ago". Every data view shows loading, error and freshness states; never show a stale number without a label.
- Keyboard navigable; sufficient contrast in light and dark mode (`prefers-color-scheme`).
