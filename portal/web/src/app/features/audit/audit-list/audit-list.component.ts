import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { SlicePipe } from '@angular/common';
import { AuditService } from '../../../core/api/audit.service';
import { AuditEntry } from '../../../core/api/models';

@Component({
  selector: 'app-audit-list',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatPaginatorModule,
    SlicePipe,
  ],
  templateUrl: './audit-list.component.html',
})
export class AuditListComponent implements OnInit {
  private readonly svc = inject(AuditService);
  private readonly fb = inject(FormBuilder);

  readonly items = signal<AuditEntry[]>([]);
  readonly total = signal(0);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly pageSize = signal(50);

  readonly filters = this.fb.group({ tenantId: [''], from: [''], to: [''], action: [''] });

  readonly columns = ['performedAt', 'action', 'actor', 'tenantId', 'details'];

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    const f = this.filters.getRawValue();
    this.svc.getAudit({
      tenantId: f.tenantId || undefined,
      from: f.from || undefined,
      to: f.to || undefined,
      action: f.action || undefined,
    }, this.page(), this.pageSize()).subscribe({
      next: (r) => { this.items.set(r.items); this.total.set(r.total); this.loading.set(false); },
      error: () => { this.error.set('Failed to load audit log.'); this.loading.set(false); },
    });
  }

  onPage(e: PageEvent): void { this.page.set(e.pageIndex + 1); this.pageSize.set(e.pageSize); this.load(); }
}
