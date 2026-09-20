import { Component, OnInit, inject, signal, input } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatTabsModule } from '@angular/material/tabs';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { NgxEchartsDirective } from 'ngx-echarts';
import { SlicePipe } from '@angular/common';
import type { EChartsOption } from 'echarts';
import { CollectorService } from '../../../core/api/collector.service';
import { CollectorDetail, HealthHistory } from '../../../core/api/models';
import { EnrolmentTokenDialogComponent } from '../enrolment-token-dialog/enrolment-token-dialog.component';
import { CollectorRevokeDialogComponent } from '../collector-revoke-dialog/collector-revoke-dialog.component';

@Component({
  selector: 'app-collector-detail',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatTabsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatTableModule,
    MatFormFieldModule,
    MatInputModule,
    MatSlideToggleModule,
    MatSelectModule,
    MatProgressSpinnerModule,
    MatDividerModule,
    NgxEchartsDirective,
    SlicePipe,
  ],
  templateUrl: './collector-detail.component.html',
})
export class CollectorDetailComponent implements OnInit {
  readonly collectorId = input.required<string>();

  private readonly svc = inject(CollectorService);
  private readonly dialog = inject(MatDialog);
  private readonly fb = inject(FormBuilder);

  readonly detail = signal<CollectorDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly commandResult = signal<string | null>(null);
  readonly commandRunning = signal(false);

  readonly healthChartOption = signal<EChartsOption>({});

  readonly certColumns = ['issuedAt', 'expiresAt', 'status'];
  readonly mappingColumns = ['sourceId', 'assetName'];
  readonly reconcColumns = ['minuteAt', 'produced', 'accepted', 'deadLettered'];

  readonly configForm = this.fb.group({
    commandChannelEnabled: [false],
    batchingJson: ['{}'],
    forwarderJson: ['{}'],
    bufferJson: ['{}'],
    intervalsJson: ['{}'],
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.getCollector(this.collectorId()).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
        this.configForm.patchValue({
          commandChannelEnabled: d.config.commandChannelEnabled,
          batchingJson: JSON.stringify(d.config.batching, null, 2),
          forwarderJson: JSON.stringify(d.config.forwarder, null, 2),
          bufferJson: JSON.stringify(d.config.buffer, null, 2),
          intervalsJson: JSON.stringify(d.config.intervals, null, 2),
        });
        this.loadHealthChart();
      },
      error: () => { this.error.set('Failed to load collector.'); this.loading.set(false); },
    });
  }

  loadHealthChart(): void {
    const now = new Date();
    const from = new Date(now.getTime() - 60 * 60 * 1000).toISOString();
    this.svc.getHealthHistory(this.collectorId(), from, now.toISOString()).subscribe({
      next: (h) => this.healthChartOption.set(this.buildHealthChart(h)),
    });
  }

  buildHealthChart(data: HealthHistory[]): EChartsOption {
    return {
      tooltip: { trigger: 'axis' },
      xAxis: { type: 'time' },
      yAxis: { type: 'value', name: 'Events' },
      series: [
        { name: 'Buffer Depth', type: 'line', data: data.map((d) => [d.receivedAt, d.bufferDepthEvents]) },
        { name: 'Buffer Capacity', type: 'line', data: data.map((d) => [d.receivedAt, d.bufferCapacityEvents]) },
      ],
    };
  }

  saveConfig(): void {
    const v = this.configForm.getRawValue();
    const body = {
      commandChannelEnabled: v.commandChannelEnabled,
      batching: JSON.parse(v.batchingJson ?? '{}'),
      forwarder: JSON.parse(v.forwarderJson ?? '{}'),
      buffer: JSON.parse(v.bufferJson ?? '{}'),
      intervals: JSON.parse(v.intervalsJson ?? '{}'),
    };
    this.svc.updateConfig(this.collectorId(), body).subscribe({ next: () => this.load() });
  }

  sendCommand(type: string): void {
    if (type === 'Restart' && !confirm('Restart the collector?')) return;
    this.commandRunning.set(true);
    this.commandResult.set(null);
    this.svc.sendCommand(this.collectorId(), type).subscribe({
      next: (r) => {
        this.commandRunning.set(false);
        this.commandResult.set(JSON.stringify(r, null, 2));
      },
      error: (err) => {
        this.commandRunning.set(false);
        this.commandResult.set(JSON.stringify(err.error ?? { error: 'Command failed' }, null, 2));
      },
    });
  }

  openToken(): void {
    this.dialog.open(EnrolmentTokenDialogComponent, {
      width: '560px',
      data: { collectorId: this.collectorId() },
    });
  }

  openRevoke(): void {
    this.dialog.open(CollectorRevokeDialogComponent, {
      width: '440px',
      data: { collectorId: this.collectorId() },
    }).afterClosed().subscribe((done) => { if (done) this.load(); });
  }
}
