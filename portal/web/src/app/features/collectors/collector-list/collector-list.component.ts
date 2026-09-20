import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { SlicePipe } from '@angular/common';
import { CollectorService } from '../../../core/api/collector.service';
import { CollectorSummary } from '../../../core/api/models';
import { CollectorRegisterDialogComponent } from '../collector-register-dialog/collector-register-dialog.component';

@Component({
  selector: 'app-collector-list',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    MatTooltipModule,
    SlicePipe,
  ],
  templateUrl: './collector-list.component.html',
})
export class CollectorListComponent implements OnInit {
  private readonly svc = inject(CollectorService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly collectors = signal<CollectorSummary[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly filters = this.fb.group({
    tenantId: [''],
    siteId: [''],
    status: [''],
  });

  readonly columns = [
    'name', 'tenantSite', 'region', 'status', 'freshness',
    'buffer', 'softwareVersion', 'configVersion', 'certExpiry', 'actions',
  ];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    const f = this.filters.getRawValue();
    this.svc.getCollectors({
      tenantId: f.tenantId || undefined,
      siteId: f.siteId || undefined,
      status: f.status || undefined,
    }).subscribe({
      next: (r) => { this.collectors.set(r.items); this.loading.set(false); },
      error: () => { this.error.set('Failed to load collectors.'); this.loading.set(false); },
    });
  }

  bufferPct(c: CollectorSummary): number {
    if (!c.bufferCapacityEvents) return 0;
    return Math.round((c.bufferDepthEvents / c.bufferCapacityEvents) * 100);
  }

  freshnessLabel(c: CollectorSummary): string {
    if (c.freshnessAgeSeconds === null) return '—';
    const s = c.freshnessAgeSeconds;
    if (s < 60) return `${s} s ago`;
    if (s < 3600) return `${Math.round(s / 60)} min ago`;
    return `${Math.round(s / 3600)} h ago`;
  }

  configDrift(c: CollectorSummary): boolean {
    return c.reportedConfigVersion !== null && c.reportedConfigVersion !== c.configVersion;
  }

  openRegister(): void {
    this.dialog.open(CollectorRegisterDialogComponent, { width: '440px' }).afterClosed().subscribe((created) => {
      if (created) this.load();
    });
  }

  openDetail(c: CollectorSummary): void {
    this.router.navigate(['/collectors', c.collectorId]);
  }
}
