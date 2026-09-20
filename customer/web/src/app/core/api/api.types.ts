export interface MeResponse {
  userId: string;
  displayName: string;
  tenantId: string;
  tenantName: string;
}

export interface AssetInfo {
  assetId: string;
  name: string;
  assetType: string;
  counterName: string;
  processValueName: string;
  processValueUnit: string;
}

export interface LineInfo {
  lineId: string;
  name: string;
  assets: AssetInfo[];
}

export interface SiteInfo {
  siteId: string;
  name: string;
  regionLabel: string;
  timeZone: string;
  lines: LineInfo[];
}

export interface HierarchyResponse {
  tenantId: string;
  tenantName: string;
  sites: SiteInfo[];
}

export interface Freshness {
  lastEventTime: string | null;
  ageSeconds: number;
  status: 'Current' | 'Stale' | 'NoData';
}

export interface CompletenessLastHour {
  produced: number;
  accepted: number;
  deadLettered: number;
  ratio: number;
}

export interface SiteStatus {
  siteId: string;
  name: string;
  freshness: Freshness;
  completenessLastHour: CompletenessLastHour;
  openGapCount: number;
}

export interface LineSummary {
  lineId: string;
  name: string;
  eventCount: number;
  counterDelta: number;
  faultEventCount: number;
  restatedMinutes: number;
}

export interface AssetRollup {
  minuteStart: string;
  eventCount: number;
  counterDelta: number;
  faultEventCount: number;
  lastState: string | null;
  processValueAvg: number | null;
  processValueMin: number | null;
  processValueMax: number | null;
  isRestated: boolean;
  restatementCount: number;
  lastComputedAt: string;
  assetId?: string;
}
