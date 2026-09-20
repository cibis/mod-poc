import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { MatChipsModule } from '@angular/material/chips';
import { TenantService } from '../../../core/api/tenant.service';
import { Tenant } from '../../../core/api/models';
import { TenantCreateDialogComponent } from '../tenant-create-dialog/tenant-create-dialog.component';

@Component({
  selector: 'app-tenant-list',
  standalone: true,
  imports: [
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule,
  ],
  templateUrl: './tenant-list.component.html',
})
export class TenantListComponent implements OnInit {
  private readonly svc = inject(TenantService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  readonly tenants = signal<Tenant[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly columns = ['name', 'slug', 'isolationMode', 'status', 'siteCount', 'collectorCount', 'actions'];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.getTenants().subscribe({
      next: (r) => { this.tenants.set(r.items); this.loading.set(false); },
      error: () => { this.error.set('Failed to load tenants.'); this.loading.set(false); },
    });
  }

  openCreate(): void {
    this.dialog.open(TenantCreateDialogComponent, { width: '420px' }).afterClosed().subscribe((created) => {
      if (created) this.load();
    });
  }

  openDetail(t: Tenant): void {
    this.router.navigate(['/tenants', t.tenantId]);
  }
}
