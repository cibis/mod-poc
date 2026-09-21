import { Component, inject, input, signal, computed, OnInit, DestroyRef } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { switchMap, EMPTY, catchError } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { EChartsOption } from 'echarts';
import { ApiService } from '../../core/api/api.service';
import { LiveHubService } from '../../core/live/live-hub.service';
import { AssetRollup } from '../../core/api/api.types';
import { RangePickerComponent, RangeMinutes } from '../../shared/range-picker/range-picker.component';

function formatLocalTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function formatUtcTime(iso: string): string {
  return new Date(iso).toISOString().replace('T', ' ').replace('Z', ' UTC');
}

@Component({
  selector: 'app-asset',
  standalone: true,
  imports: [
    MatCardModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    NgxEchartsDirective,
    RangePickerComponent,
  ],
  templateUrl: './asset.component.html',
  styleUrl: './asset.component.scss',
})
export class AssetComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly liveHub = inject(LiveHubService);
  private readonly destroyRef = inject(DestroyRef);

  readonly assetId = input.required<string>();

  readonly rangeMinutes = signal<RangeMinutes>(60);
  readonly rollups = signal<AssetRollup[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly rangeDates = computed(() => {
    const m = this.rangeMinutes();
    const to = new Date();
    const from = new Date(to.getTime() - m * 60_000);
    return { from: from.toISOString(), to: to.toISOString() };
  });

  readonly chartOptions = computed((): EChartsOption => {
    const data = this.rollups();
    if (!data.length) return {};
    return {
      tooltip: { trigger: 'axis', axisPointer: { type: 'cross' } },
      legend: { data: ['Counter Delta', 'Process Value'] },
      grid: { top: 50, right: 60, bottom: 30, left: 60, containLabel: false },
      xAxis: {
        type: 'category',
        data: data.map(r => formatLocalTime(r.minuteStart)),
        axisLabel: { rotate: 45 },
      },
      yAxis: [
        { type: 'value', name: 'Count', position: 'left' },
        { type: 'value', name: 'Process', position: 'right' },
      ],
      series: [
        {
          name: 'Counter Delta',
          type: 'bar',
          data: data.map(r => ({
            value: r.counterDelta,
            itemStyle: { color: r.isRestated ? '#FF9800' : '#1976D2' },
          })),
        },
        {
          name: 'Process Value',
          type: 'line',
          yAxisIndex: 1,
          data: data.map(r => r.processValueAvg),
          connectNulls: false,
          smooth: true,
        },
      ],
    };
  });

  private readonly _rollupsEffect = toObservable(this.rangeDates)
    .pipe(
      switchMap(({ from, to }) =>
        this.api.getAssetRollups(this.assetId(), from, to).pipe(
          catchError(err => {
            const status = (err as { status?: number }).status;
            this.error.set(status === 404 ? 'Not found' : 'Failed to load rollups');
            return EMPTY;
          }),
        ),
      ),
      takeUntilDestroyed(),
    )
    .subscribe(r => {
      this.rollups.set(r);
      this.loading.set(false);
      this.error.set(null);
    });

  ngOnInit(): void {
    this.liveHub.start();
    this.liveHub.rollupsUpdated$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(event => {
        const mine = event.items.filter(i => i.assetId === this.assetId());
        if (!mine.length) return;
        this.rollups.update(current => {
          const map = new Map(current.map(r => [r.minuteStart, r]));
          for (const item of mine) map.set(item.minuteStart, item);
          return [...map.values()].sort((a, b) => a.minuteStart.localeCompare(b.minuteStart));
        });
      });
  }

  formatUtc(iso: string): string { return formatUtcTime(iso); }
}
