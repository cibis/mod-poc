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
import { RestatementService } from '../../../core/api/restatement.service';
import { Restatement } from '../../../core/api/models';

@Component({
  selector: 'app-restatement-list',
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
  templateUrl: './restatement-list.component.html',
})
export class RestatementListComponent implements OnInit {
  private readonly svc = inject(RestatementService);
  private readonly fb = inject(FormBuilder);

  readonly items = signal<Restatement[]>([]);
  readonly total = signal(0);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly pageSize = signal(50);

  readonly filters = this.fb.group({ tenantId: [''], from: [''], to: [''] });

  readonly columns = ['minuteAt', 'assetName', 'lineName', 'siteName', 'eventCount', 'lastComputedAt'];

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    const f = this.filters.getRawValue();
    this.svc.getRestatements(f.tenantId || undefined, f.from || undefined, f.to || undefined, this.page(), this.pageSize()).subscribe({
      next: (r) => { this.items.set(r.items); this.total.set(r.total); this.loading.set(false); },
      error: () => { this.error.set('Failed to load restatements.'); this.loading.set(false); },
    });
  }

  onPage(e: PageEvent): void { this.page.set(e.pageIndex + 1); this.pageSize.set(e.pageSize); this.load(); }
}
