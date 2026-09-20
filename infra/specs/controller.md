# Spec: controller space (`controller/main.bicep`)

Part of the `infra` module. Module rules and layout: `infra/CLAUDE.md`.

`controller/main.bicep`:
- Own Log Analytics workspace (1 GB cap) and Container Apps environment (Consumption) in `--controller-location`.
- User-assigned identity `id-collector-pull` with AcrPull on the production registry (cross-resource-group role assignment through a module scoped to the production resource group).
- Contributor for `id-portal` on `rg-{prefix}-ctrl` (so the simulator can create and delete collector apps and assign `id-collector-pull`).
- No collector apps are created by infra. The portal creates them at runtime (container apps named `col-…`, tagged `mod-sim-collector`).
