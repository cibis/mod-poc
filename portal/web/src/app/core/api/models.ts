export interface PagedResult<T> {
  items: T[];
  total: number;
}

export interface Tenant {
  tenantId: string;
  name: string;
  slug: string;
  isolationMode: string;
  status: string;
  siteCount: number;
  collectorCount: number;
}

export interface Asset {
  assetId: string;
  name: string;
  assetType: string;
  processValueName: string;
  processValueUnit: string;
  processValueMin: number | null;
  processValueMax: number | null;
}

export interface Line {
  lineId: string;
  name: string;
  assets: Asset[];
}

export interface Site {
  siteId: string;
  name: string;
  regionLabel: string;
  timeZone: string;
  lines: Line[];
  collectors: CollectorSummary[];
}

export interface TenantHierarchy {
  tenant: Tenant;
  sites: Site[];
}

export interface CollectorSummary {
  collectorId: string;
  name: string;
  tenantId: string;
  tenantName: string;
  siteId: string;
  siteName: string;
  regionLabel: string;
  status: string;
  lastSeenAt: string | null;
  softwareVersion: string | null;
  reportedConfigVersion: number | null;
  configVersion: number;
  certificateExpiresAt: string | null;
  bufferDepthEvents: number;
  bufferCapacityEvents: number;
  freshnessAgeSeconds: number | null;
}

export interface Certificate {
  certificateId: string;
  issuedAt: string;
  expiresAt: string;
  supersededAt: string | null;
  revokedAt: string | null;
  status: string;
}

export interface CollectorConfig {
  version: number;
  batching: Record<string, unknown>;
  forwarder: Record<string, unknown>;
  buffer: Record<string, unknown>;
  intervals: Record<string, unknown>;
  commandChannelEnabled: boolean;
}

export interface CollectorMapping {
  mappingId: string;
  sourceId: string;
  assetId: string;
  assetName: string;
  validFrom: string;
  validTo: string | null;
}

export interface CollectorHealth {
  receivedAt: string;
  bufferDepthEvents: number;
  bufferCapacityEvents: number;
  overflowDroppedTotal: number;
}

export interface Reconciliation {
  minuteAt: string;
  produced: number;
  accepted: number;
  deadLettered: number;
}

export interface CollectorDetail {
  collectorId: string;
  name: string;
  tenantId: string;
  tenantName: string;
  siteId: string;
  siteName: string;
  regionLabel: string;
  status: string;
  lastSeenAt: string | null;
  softwareVersion: string | null;
  config: CollectorConfig;
  mappings: CollectorMapping[];
  certificates: Certificate[];
  latestHealth: CollectorHealth | null;
  reconciliation: Reconciliation[];
}

export interface EnrolmentToken {
  token: string;
  expiresAt: string;
}

export interface CommandResult {
  requestId: string;
  outcome: string;
  result: unknown;
  error: string | null;
  elapsedMs: number;
}

export interface HealthHistory {
  receivedAt: string;
  bufferDepthEvents: number;
  bufferCapacityEvents: number;
  overflowDroppedTotal: number;
}

export interface DeadLetter {
  id: string;
  tenantId: string;
  tenantName: string;
  collectorId: string;
  collectorName: string;
  reasonCode: string;
  status: string;
  receivedAt: string;
  eventJson: string | null;
}

export interface Restatement {
  minuteAt: string;
  assetId: string;
  assetName: string;
  lineId: string;
  lineName: string;
  siteId: string;
  siteName: string;
  eventCount: number;
  lastComputedAt: string;
}

export interface AuditEntry {
  auditId: string;
  performedAt: string;
  action: string;
  actor: string;
  tenantId: string | null;
  details: string | null;
}

export interface BulkReplayResult {
  updated: number;
}
