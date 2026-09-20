# Contract: Database — schema `sim` (demo scaffolding; used only by portal)

> Shared contract. Read only if your module's task table lists it. Do not change without the user's approval; see the contract index in the root `CLAUDE.md` for consumers.

Conventions: see `overview.md` (types, timestamps, GUIDs).

**sim.CollectorState** — PK `CollectorId`
`CollectorId` · `PoweredOn` bit · `ContainerAppName` varchar(32) null · `ProvisioningState` varchar(20) (`Off`, `Provisioning`, `Running`, `Deprovisioning`, `Failed`) · `LastError` nvarchar(1000) null · `BufferOverflowEver` bit (sticky; never reset) · `LastStatusJson` nvarchar(max) null · `LastStatusAt` null · `UpdatedAt`

**sim.ScenarioRun** — PK `ScenarioRunId`
`ScenarioRunId` · `ScenarioName` varchar(50) · `ParametersJson` nvarchar(max) · `StartedAt` · `EndedAt` null · `Status` (`Running`, `Completed`, `Stopped`, `Failed`) · `StartedBy` nvarchar(200)

**sim.TimelineMarker** — PK `MarkerId` bigint identity; index (`At`)
`MarkerId` · `At` · `Kind` (`Command`, `Power`, `ScenarioStart`, `ScenarioStep`, `ScenarioEnd`) · `Label` nvarchar(200) · `TargetsJson` nvarchar(max) · `ScenarioRunId` null

**sim.MetricSample** — PK (`MetricKey`, `At`)
`MetricKey` varchar(150) · `At` datetime2(0) · `Value` float. Written every 10 s; purged after 24 h.
