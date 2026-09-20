// DEMO SCAFFOLDING

export type NodeKind =
  | 'collector' | 'ingest' | 'eventHub' | 'processing' | 'sql'
  | 'management' | 'reporting' | 'portal' | 'cmdBus' | 'simBus';

export type NodeSpace = 'controller' | 'production' | 'outside';
export type NodeState = 'ok' | 'degraded' | 'down' | 'off' | 'scaling';
export type EdgeKind = 'data' | 'management' | 'command' | 'simulation';
export type ProvisioningState = 'off' | 'starting' | 'running' | 'stopping' | 'failed';

export interface TopologyNode {
  id: string;
  kind: NodeKind;
  label: string;
  space: NodeSpace;
  state: NodeState;
  replicas?: number;
  minReplicas?: number;
  maxReplicas?: number;
  metrics: Record<string, number>;
}

export interface TopologyEdge {
  id: string;
  from: string;
  to: string;
  kind: EdgeKind;
  active: boolean;
  label: string;
}

export interface TopologySnapshot {
  nodes: TopologyNode[];
  edges: TopologyEdge[];
}

export interface SimCollector {
  collectorId: string;
  name: string;
  tenantId: string;
  tenantName: string;
  siteId: string;
  siteName: string;
  regionLabel: string;
  status: string;
  poweredOn: boolean;
  provisioningState: ProvisioningState;
  lastError: string | null;
  bufferOverflowEver: boolean;
  simStatus: SimStatusBody | null;
  lastStatusAt: string | null;
}

export interface SimStatusBody {
  bufferDepth: number;
  bufferCapacity: number;
  linkUp: boolean;
  paused: boolean;
  rateEventsPerSec: number;
  drainRatePerSec: number;
}

export interface TargetSelector {
  collectorIds?: string[];
  siteIds?: string[];
  tenantId?: string;
  regionLabel?: string;
  all?: boolean;
}

export interface PowerResponse {
  accepted: string[];
  skipped: Array<{ collectorId: string; reason: string }>;
}

export interface CommandResponse {
  commandId: string;
  sentTo: string[];
}

export interface MetricSeries {
  key: string;
  points: [number, number][]; // [epochMs, value]
}

export interface MetricsResponse {
  series: MetricSeries[];
}

export interface TimelineMarker {
  markerId: string;
  at: string;
  kind: string;
  label: string;
  targets: TargetSelector | null;
  scenarioRunId: string | null;
}

export interface ScenarioParameter {
  name: string;
  type: string;
  default: unknown;
}

export interface Scenario {
  name: string;
  description: string;
  parameters: ScenarioParameter[];
  durationSeconds: number;
}

export type ScenarioRunStatus = 'running' | 'completed' | 'stopped' | 'failed';

export interface ScenarioRun {
  scenarioRunId: string;
  name: string;
  status: ScenarioRunStatus;
  currentStep: string | null;
  startedAt: string;
  endedAt: string | null;
}

export interface GenerateFleetRequest {
  tenantId: string;
  sites: number;
  linesPerSite: number;
  assetsPerLine: number;
  regionLabel: string;
  commandChannelEnabled: boolean;
}

export type WindowMinutes = 5 | 15 | 30 | 60;

export interface HubMetricsTick {
  at: string;
  values: Record<string, number>;
}

export interface HubCollectorStatus {
  collectorId: string;
  status: SimStatusBody;
}

export interface HubProvisioning {
  collectorId: string;
  provisioningState: ProvisioningState;
  lastError: string | null;
}
