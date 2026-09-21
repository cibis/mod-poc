// DEMO SCAFFOLDING
import { Component, computed, inject } from '@angular/core';
import { NgxEchartsDirective } from 'ngx-echarts';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { EChartsOption } from 'echarts';
import { SimStoreService } from '../../services/sim-store.service';
import { SimCollector } from '../../models';

@Component({
  selector: 'app-buffers',
  standalone: true,
  imports: [NgxEchartsDirective, MatCardModule, MatProgressSpinnerModule],
  templateUrl: './buffers.component.html',
  styleUrl: './buffers.component.scss',
})
export class BuffersComponent {
  readonly store = inject(SimStoreService);

  readonly poweredCollectors = computed(() => this.store.collectors().filter((c) => c.poweredOn));

  gaugeOptions(c: SimCollector): EChartsOption {
    const depth = c.simStatus?.bufferDepth ?? 0;
    const cap = c.simStatus?.bufferCapacity ?? 1;
    const pct = Math.min(100, Math.round((depth / cap) * 100));
    const color = pct > 90 ? '#c62828' : pct > 75 ? '#ef6c00' : '#43a047';
    return {
      series: [{
        type: 'gauge',
        min: 0, max: 100,
        radius: '85%',
        axisLine: { lineStyle: { color: [[pct / 100, color], [1, '#e0e0e0']], width: 18 } },
        axisTick: { show: false },
        splitLine: { show: false },
        axisLabel: { show: false },
        pointer: { length: '60%', width: 6 },
        detail: { formatter: '{value}%', fontSize: 22, fontWeight: 'bold', offsetCenter: [0, '55%'] },
        title: { fontSize: 13, offsetCenter: [0, '80%'] },
        data: [{ value: pct, name: `${depth} / ${cap}` }],
      }],
    } as EChartsOption;
  }

  sparklineOptions(c: SimCollector): EChartsOption {
    const series = this.store.metricSeries();
    const depthKey = `collector.${c.collectorId}.bufferDepth`;
    const points = series.get(depthKey) ?? [];
    return {
      xAxis: { type: 'time', show: false },
      yAxis: { type: 'value', show: false, scale: true },
      grid: { left: 0, right: 0, top: 4, bottom: 4 },
      series: [{ type: 'line', data: points, showSymbol: false, lineStyle: { width: 1.5 } }],
    } as EChartsOption;
  }

  drainRate(c: SimCollector): string {
    const r = c.simStatus?.drainRatePerSec;
    return r !== undefined ? `${r}/s` : '—';
  }

  linkState(c: SimCollector): string {
    if (!c.simStatus) return '—';
    return c.simStatus.linkUp ? 'Up' : 'Down';
  }
}
