// DEMO SCAFFOLDING
import { Component, computed, inject } from '@angular/core';
import { NgxEchartsDirective } from 'ngx-echarts';
import { MatCardModule } from '@angular/material/card';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FormsModule } from '@angular/forms';
import { EChartsOption, GraphSeriesOption } from 'echarts';
import { SimStoreService } from '../../services/sim-store.service';
import { TopologyNode, NodeState, WindowMinutes } from '../../models';

const STATE_COLOR: Record<NodeState, string> = {
  ok: '#43a047',
  degraded: '#ffa726',
  down: '#e53935',
  off: '#9e9e9e',
  scaling: '#29b6f6',
};

@Component({
  selector: 'app-overview',
  standalone: true,
  imports: [NgxEchartsDirective, MatCardModule, MatSelectModule, MatProgressSpinnerModule, FormsModule],
  templateUrl: './overview.component.html',
  styleUrl: './overview.component.scss',
})
export class OverviewComponent {
  readonly store = inject(SimStoreService);

  readonly windowOptions: { label: string; value: WindowMinutes }[] = [
    { label: '5 min', value: 5 },
    { label: '15 min', value: 15 },
    { label: '30 min', value: 30 },
    { label: '60 min', value: 60 },
  ];

  get windowMinutes(): WindowMinutes { return this.store.windowMinutes(); }
  set windowMinutes(v: WindowMinutes) { this.store.windowMinutes.set(v); }

  readonly topologyOptions = computed<EChartsOption>(() => this.buildTopology());
  readonly ingestChartOptions = computed<EChartsOption>(() =>
    this.buildLineChart('Ingest batches/s', 'ingest.batchesPerSec'));
  readonly lagChartOptions = computed<EChartsOption>(() =>
    this.buildLineChart('Event Hubs lag (batches)', 'eventhub.lagBatches'));
  readonly replicasChartOptions = computed<EChartsOption>(() => this.buildReplicasChart());
  readonly processingChartOptions = computed<EChartsOption>(() =>
    this.buildLineChart('Processing events/s', 'processing.eventsPerSec'));
  readonly deadLettersChartOptions = computed<EChartsOption>(() =>
    this.buildLineChart('Dead letters/min', 'processing.deadLettersPerMin'));
  readonly restatedChartOptions = computed<EChartsOption>(() =>
    this.buildLineChart('Restated minutes/hr', 'rollups.restatedLastHour'));

  private buildTopology(): EChartsOption {
    const snap = this.store.topology();

    // Fixed positions for each node kind
    const positions: Record<string, [number, number]> = {
      // Controller zone (left)
      collector: [120, 300],
      // Production zone (right)
      management: [420, 120],
      cmdBus:     [420, 200],
      ingest:     [560, 280],
      eventHub:   [700, 280],
      processing: [840, 280],
      sql:        [840, 420],
      reporting:  [980, 280],
      portal:     [980, 420],
      // Outside (amber) - sim bus at bottom
      simBus:     [560, 520],
    };

    // Spread collectors by region
    const collectors = snap.nodes.filter((n) => n.kind === 'collector');
    const regions = [...new Set(collectors.map((c) => c.label.split(' ')[0] ?? 'region'))];

    const graphNodes: GraphSeriesOption['data'] = snap.nodes.map((n, i) => {
      let x: number, y: number;
      if (n.kind === 'collector') {
        const ci = collectors.indexOf(n);
        const regionIdx = regions.indexOf(n.label.split(' ')[0] ?? '');
        x = 80 + (regionIdx % 2) * 100;
        y = 80 + Math.floor(ci / 2) * 80;
      } else {
        [x, y] = positions[n.kind] ?? [300 + i * 40, 300];
      }
      const replicas = n.replicas != null ? `\n×${n.replicas}` : '';
      return {
        id: n.id,
        name: n.label + replicas,
        x,
        y,
        itemStyle: { color: STATE_COLOR[n.state] ?? '#90a4ae' },
        symbolSize: n.space === 'production' ? 40 : 32,
        label: {
          show: true,
          fontSize: 10,
          position: n.space === 'controller' ? 'right' : 'bottom',
        },
      };
    });

    const graphEdges: GraphSeriesOption['links'] = snap.edges.map((e) => ({
      source: e.from,
      target: e.to,
      lineStyle: {
        color: e.kind === 'simulation' ? '#ffb300' : '#90a4ae',
        type: e.kind === 'simulation' ? 'dashed' : 'solid',
        width: e.active ? 2 : 1,
        opacity: e.active ? 1 : 0.4,
      },
    }));

    return {
      graphic: this.buildZoneGraphics(),
      series: [{
        type: 'graph',
        layout: 'none',
        roam: true,
        data: graphNodes,
        links: graphEdges,
        lineStyle: { curveness: 0 },
        emphasis: { focus: 'adjacency' },
      }],
    } as EChartsOption;
  }

  private buildZoneGraphics(): unknown[] {
    return [
      // Controller zone
      { type: 'rect', shape: { x: 20, y: 20, width: 260, height: 560 },
        style: { fill: 'rgba(33,150,243,0.05)', stroke: '#90caf9', lineWidth: 1, lineDash: [4, 4] } },
      { type: 'text', style: { text: 'Controller space', x: 28, y: 28, font: '11px sans-serif', fill: '#1565c0' } },
      // Production zone
      { type: 'rect', shape: { x: 360, y: 60, width: 680, height: 440 },
        style: { fill: 'rgba(76,175,80,0.05)', stroke: '#a5d6a7', lineWidth: 1, lineDash: [4, 4] } },
      { type: 'text', style: { text: 'Production space', x: 368, y: 70, font: '11px sans-serif', fill: '#1b5e20' } },
      // Sim bus zone (outside, amber dashed)
      { type: 'rect', shape: { x: 480, y: 480, width: 200, height: 80 },
        style: { fill: 'rgba(255,179,0,0.08)', stroke: '#ffb300', lineWidth: 2, lineDash: [6, 3] } },
      { type: 'text', style: { text: 'demo channel — not part of the modelled network', x: 488, y: 490, font: '10px sans-serif', fill: '#e65100' } },
    ];
  }

  private buildLineChart(title: string, metricKey: string): EChartsOption {
    const series = this.store.metricSeries();
    const points = series.get(metricKey) ?? [];
    const markers = this.store.timelineMarkers();

    const markLines = markers.map((m) => ({
      xAxis: new Date(m.at).getTime(),
      label: { formatter: () => m.label, show: true, fontSize: 9 },
      lineStyle: { color: '#ff8f00', type: 'dashed' as const },
    }));

    return {
      title: { text: title, textStyle: { fontSize: 12, fontWeight: 'normal' } },
      xAxis: { type: 'time', axisLabel: { fontSize: 10 } },
      yAxis: { type: 'value', axisLabel: { fontSize: 10 }, scale: true },
      grid: { left: 50, right: 16, top: 36, bottom: 36 },
      series: [{
        type: 'line',
        data: points,
        showSymbol: false,
        smooth: false,
        markLine: { silent: true, data: markLines.map((ml) => [ml, ml]) },
      }],
      tooltip: { trigger: 'axis', formatter: (params: unknown) => {
        const p = (params as Array<{ axisValue: number; value: [number, number] }>)[0];
        if (!p) return '';
        return `${new Date(p.axisValue).toLocaleTimeString()}<br/>${p.value[1].toFixed(2)}`;
      }},
    } as EChartsOption;
  }

  private buildReplicasChart(): EChartsOption {
    const series = this.store.metricSeries();
    const markers = this.store.timelineMarkers();
    const appKeys = ['ingest.replicas', 'management.replicas', 'processing.replicas', 'portal.replicas', 'reporting.replicas'];
    const appLabels: Record<string, string> = {
      'ingest.replicas': 'Ingest',
      'management.replicas': 'Management',
      'processing.replicas': 'Processing',
      'portal.replicas': 'Portal',
      'reporting.replicas': 'Reporting',
    };

    const markLines = markers.map((m) => ({
      xAxis: new Date(m.at).getTime(),
      lineStyle: { color: '#ff8f00', type: 'dashed' as const },
    }));

    const seriesList = appKeys.map((key) => ({
      type: 'line' as const,
      name: appLabels[key],
      data: series.get(key) ?? [],
      showSymbol: false,
    }));

    return {
      title: { text: 'Running replicas per app', textStyle: { fontSize: 12, fontWeight: 'normal' } },
      xAxis: { type: 'time', axisLabel: { fontSize: 10 } },
      yAxis: { type: 'value', axisLabel: { fontSize: 10 }, minInterval: 1 },
      grid: { left: 50, right: 16, top: 72, bottom: 36 },
      legend: { right: 0, top: 28, orient: 'horizontal', textStyle: { fontSize: 10 } },
      series: seriesList.map((s) => ({
        ...s,
        markLine: s.name === 'Ingest'
          ? { silent: true, data: markLines.map((ml) => [ml, ml]) }
          : undefined,
      })),
      tooltip: { trigger: 'axis' },
    } as EChartsOption;
  }

  nodeLabel(n: TopologyNode): string {
    const r = n.replicas != null ? ` ×${n.replicas}` : '';
    return n.label + r;
  }
}
