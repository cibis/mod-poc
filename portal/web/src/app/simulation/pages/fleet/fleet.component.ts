// DEMO SCAFFOLDING
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { SelectionModel } from '@angular/cdk/collections';
import { SimStoreService } from '../../services/sim-store.service';
import { SimApiService } from '../../services/sim-api.service';
import { SimCollector, TargetSelector } from '../../models';
import { FleetGeneratorDialogComponent } from '../../components/fleet-generator-dialog/fleet-generator-dialog.component';
import { SetCommandDialogComponent, CommandDialogData } from '../../components/set-command-dialog/set-command-dialog.component';

type TargetMode = 'selection' | 'site' | 'tenant' | 'region' | 'all';

@Component({
  selector: 'app-fleet',
  standalone: true,
  imports: [
    FormsModule, MatTableModule, MatCheckboxModule, MatButtonModule, MatIconModule,
    MatSelectModule, MatProgressSpinnerModule, MatTooltipModule, MatMenuModule,
  ],
  templateUrl: './fleet.component.html',
  styleUrl: './fleet.component.scss',
})
export class FleetComponent {
  readonly store = inject(SimStoreService);
  private readonly api = inject(SimApiService);
  private readonly dialog = inject(MatDialog);

  readonly columns = [
    'select', 'name', 'tenant', 'site', 'region', 'status',
    'power', 'provisioning', 'link', 'rate', 'paused', 'buffer', 'overflow',
  ];

  readonly selection = new SelectionModel<string>(true, []);
  readonly targetMode = signal<TargetMode>('selection');
  readonly actionBusy = signal(false);
  readonly actionError = signal<string | null>(null);

  // Unique values for target selectors
  readonly sites = computed(() => [...new Set(this.store.collectors().map((c) => ({ id: c.siteId, name: c.siteName })))].filter(
    (v, i, a) => a.findIndex((x) => x.id === v.id) === i,
  ));
  readonly tenants = computed(() => [...new Set(this.store.collectors().map((c) => ({ id: c.tenantId, name: c.tenantName })))].filter(
    (v, i, a) => a.findIndex((x) => x.id === v.id) === i,
  ));
  readonly regions = computed(() => [...new Set(this.store.collectors().map((c) => c.regionLabel))]);

  readonly selectedSiteId = signal<string>('');
  readonly selectedTenantId = signal<string>('');
  readonly selectedRegion = signal<string>('');

  bufferPct(c: SimCollector): number {
    const s = c.simStatus;
    if (!s || !s.bufferCapacity) return 0;
    return Math.round((s.bufferDepth / s.bufferCapacity) * 100);
  }

  isAllSelected(): boolean {
    return this.selection.selected.length === this.store.collectors().length;
  }

  masterToggle(): void {
    if (this.isAllSelected()) {
      this.selection.clear();
    } else {
      this.store.collectors().forEach((c) => this.selection.select(c.collectorId));
    }
  }

  private buildTargets(): TargetSelector {
    switch (this.targetMode()) {
      case 'selection': return { collectorIds: this.selection.selected };
      case 'site':      return { siteIds: [this.selectedSiteId()] };
      case 'tenant':    return { tenantId: this.selectedTenantId() };
      case 'region':    return { regionLabel: this.selectedRegion() };
      case 'all':       return { all: true };
    }
  }

  powerOn(): void  { this.doPower(true); }
  powerOff(): void { this.doPower(false); }

  private doPower(on: boolean): void {
    this.actionBusy.set(true);
    this.api.postPower(this.buildTargets(), on).subscribe({
      next: () => { this.actionBusy.set(false); this.store.refreshCollectors(); },
      error: (e) => { this.actionBusy.set(false); this.actionError.set(e.error?.title ?? 'Failed.'); },
    });
  }

  setLinkUp(): void   { this.sendCmd('setLink', [{ name: 'up', label: 'Link up', type: 'checkbox', default: true }], 'Set Link Up'); }
  setLinkDown(): void { this.sendCmd('setLink', [{ name: 'up', label: 'Link up', type: 'checkbox', default: false }], 'Set Link Down'); }

  setRate(): void {
    this.sendCmd('setRate', [{ name: 'eventsPerSec', label: 'Events/sec', type: 'number', default: 100 }], 'Set Rate');
  }

  pauseCollectors(): void   { this.sendCmd('setPause', [{ name: 'paused', label: 'Paused', type: 'checkbox', default: true }], 'Pause'); }
  resumeCollectors(): void  { this.sendCmd('setPause', [{ name: 'paused', label: 'Paused', type: 'checkbox', default: false }], 'Resume'); }

  setBufferCapacity(): void {
    this.sendCmd('setBufferCapacity', [{ name: 'capacity', label: 'Capacity (events)', type: 'number', default: 10000 }], 'Set Buffer Capacity');
  }

  setInvalidShare(): void {
    this.sendCmd('setInvalidShare', [
      { name: 'share', label: 'Invalid share (0–1)', type: 'number', default: 0.1 },
      { name: 'kinds', label: 'Kinds (comma-separated)', type: 'text', default: 'MissingAsset' },
    ], 'Set Invalid Share');
  }

  private sendCmd(
    commandType: string,
    fields: CommandDialogData['fields'],
    title: string,
  ): void {
    const data: CommandDialogData = { title, commandType, fields, targets: this.buildTargets() };
    this.dialog.open(SetCommandDialogComponent, { data, width: '360px' })
      .afterClosed().subscribe((ok) => { if (ok) this.store.refreshCollectors(); });
  }

  openGenerator(): void {
    this.dialog.open(FleetGeneratorDialogComponent, { width: '400px' })
      .afterClosed().subscribe((r) => { if (r) this.store.refreshCollectors(); });
  }
}
