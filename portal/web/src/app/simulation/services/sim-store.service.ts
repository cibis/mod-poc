// DEMO SCAFFOLDING
import { Injectable, computed, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import {
  SimCollector, TopologySnapshot, TimelineMarker, ScenarioRun,
  MetricSeries, WindowMinutes, HubMetricsTick, HubCollectorStatus,
  HubProvisioning,
} from '../models';
import { SimApiService } from './sim-api.service';

export type HubState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

const METRIC_KEYS = [
  'ingest.batchesPerSec',
  'eventhub.lagBatches',
  'ingest.replicas',
  'processing.replicas',
  'management.replicas',
  'portal.replicas',
  'reporting.replicas',
  'processing.eventsPerSec',
  'processing.deadLettersPerMin',
  'rollups.restatedLastHour',
  'collectors.poweredOn',
  'collectors.linkDown',
];

@Injectable({ providedIn: 'root' })
export class SimStoreService {
  private readonly api = inject(SimApiService);

  readonly collectors = signal<SimCollector[]>([]);
  readonly topology = signal<TopologySnapshot>({ nodes: [], edges: [] });
  readonly metricSeries = signal<Map<string, [number, number][]>>(new Map());
  readonly timelineMarkers = signal<TimelineMarker[]>([]);
  readonly scenarioRuns = signal<ScenarioRun[]>([]);
  readonly windowMinutes = signal<WindowMinutes>(30);
  readonly hubState = signal<HubState>('disconnected');
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly showReconnected = signal(false);

  readonly productionNodes = computed(() =>
    this.topology().nodes.filter((n) => n.space === 'production' && n.replicas !== undefined),
  );

  readonly poweredCollectors = computed(() =>
    this.collectors().filter((c) => c.poweredOn),
  );

  setHubState(s: HubState): void {
    this.hubState.set(s);
  }

  setReconnected(): void {
    this.showReconnected.set(true);
    setTimeout(() => this.showReconnected.set(false), 4000);
  }

  reload(): void {
    const now = new Date();
    const from = new Date(now.getTime() - this.windowMinutes() * 60_000);
    forkJoin({
      collectors: this.api.getCollectors(),
      topology: this.api.getTopology(),
      metrics: this.api.getMetrics(METRIC_KEYS, from, now),
      timeline: this.api.getTimeline(from, now),
      runs: this.api.getScenarioRuns(20),
    }).subscribe({
      next: ({ collectors, topology, metrics, timeline, runs }) => {
        this.collectors.set(collectors);
        this.topology.set(topology);
        this.applyHistoricMetrics(metrics.series);
        this.timelineMarkers.set(timeline);
        this.scenarioRuns.set(runs);
        this.loading.set(false);
        this.error.set(null);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Failed to load simulator data.');
      },
    });
  }

  applyMetricsTick(tick: HubMetricsTick): void {
    const nowMs = new Date(tick.at).getTime();
    const cutoffMs = nowMs - this.windowMinutes() * 60_000;
    const updated = new Map(this.metricSeries());
    for (const [key, value] of Object.entries(tick.values)) {
      const existing = (updated.get(key) ?? []).filter(([t]) => t >= cutoffMs);
      existing.push([nowMs, value]);
      updated.set(key, existing);
    }
    this.metricSeries.set(updated);
  }

  applyTopology(snap: TopologySnapshot): void {
    this.topology.set(snap);
  }

  applyCollectorStatus(ev: HubCollectorStatus): void {
    this.collectors.update((list) =>
      list.map((c) =>
        c.collectorId === ev.collectorId
          ? { ...c, simStatus: ev.status, lastStatusAt: new Date().toISOString() }
          : c,
      ),
    );
  }

  applyProvisioning(ev: HubProvisioning): void {
    this.collectors.update((list) =>
      list.map((c) =>
        c.collectorId === ev.collectorId
          ? { ...c, provisioningState: ev.provisioningState, lastError: ev.lastError }
          : c,
      ),
    );
  }

  applyTimelineMarker(m: TimelineMarker): void {
    this.timelineMarkers.update((list) => [...list, m]);
  }

  applyScenarioRun(run: ScenarioRun): void {
    this.scenarioRuns.update((list) => {
      const idx = list.findIndex((r) => r.scenarioRunId === run.scenarioRunId);
      if (idx >= 0) {
        const next = [...list];
        next[idx] = run;
        return next;
      }
      return [run, ...list];
    });
  }

  refreshCollectors(): void {
    this.api.getCollectors().subscribe({ next: (c) => this.collectors.set(c) });
  }

  private applyHistoricMetrics(series: MetricSeries[]): void {
    const map = new Map(this.metricSeries());
    for (const s of series) {
      map.set(s.key, s.points);
    }
    this.metricSeries.set(map);
  }
}
