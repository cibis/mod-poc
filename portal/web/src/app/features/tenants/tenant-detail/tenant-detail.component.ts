import { Component, OnInit, inject, signal, input } from '@angular/core';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatListModule } from '@angular/material/list';
import { MatDialog } from '@angular/material/dialog';
import { TenantService } from '../../../core/api/tenant.service';
import { TenantHierarchy } from '../../../core/api/models';
import { SiteFormDialogComponent } from '../site-form-dialog/site-form-dialog.component';
import { LineFormDialogComponent } from '../line-form-dialog/line-form-dialog.component';
import { AssetFormDialogComponent } from '../asset-form-dialog/asset-form-dialog.component';

@Component({
  selector: 'app-tenant-detail',
  standalone: true,
  imports: [
    MatExpansionModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatListModule,
  ],
  templateUrl: './tenant-detail.component.html',
})
export class TenantDetailComponent implements OnInit {
  readonly tenantId = input.required<string>();

  private readonly svc = inject(TenantService);
  private readonly dialog = inject(MatDialog);

  readonly hierarchy = signal<TenantHierarchy | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.getHierarchy(this.tenantId()).subscribe({
      next: (h) => { this.hierarchy.set(h); this.loading.set(false); },
      error: () => { this.error.set('Failed to load hierarchy.'); this.loading.set(false); },
    });
  }

  addSite(): void {
    this.dialog
      .open(SiteFormDialogComponent, { width: '440px' })
      .afterClosed()
      .subscribe((body) => {
        if (!body) return;
        this.svc.createSite(this.tenantId(), body).subscribe({ next: () => this.load() });
      });
  }

  addLine(siteId: string): void {
    this.dialog
      .open(LineFormDialogComponent, { width: '380px' })
      .afterClosed()
      .subscribe((body) => {
        if (!body) return;
        this.svc.createLine(siteId, body).subscribe({ next: () => this.load() });
      });
  }

  addAsset(lineId: string): void {
    this.dialog
      .open(AssetFormDialogComponent, { width: '480px' })
      .afterClosed()
      .subscribe((body) => {
        if (!body) return;
        this.svc.createAsset(lineId, body).subscribe({ next: () => this.load() });
      });
  }
}
