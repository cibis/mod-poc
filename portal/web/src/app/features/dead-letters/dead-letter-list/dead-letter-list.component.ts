import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { SelectionModel } from '@angular/cdk/collections';
import { MatTableModule } from '@angular/material/table';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { SlicePipe } from '@angular/common';
import { DeadLetterService, DeadLetterFilters } from '../../../core/api/dead-letter.service';
import { DeadLetter } from '../../../core/api/models';

const REASON_CODES = [
  'BatchUnreadable', 'SchemaInvalid', 'UnsupportedSchemaVersion', 'UnknownEventType',
  'UnmappedSource', 'TimestampOutOfRange', 'CounterDecreaseWithoutReset', 'ValueOutOfRange',
];

@Component({
  selector: 'app-dead-letter-list',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatTableModule,
    MatCheckboxModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressSpinnerModule,
    MatPaginatorModule,
    SlicePipe,
  ],
  templateUrl: './dead-letter-list.component.html',
})
export class DeadLetterListComponent implements OnInit {
  private readonly svc = inject(DeadLetterService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly reasonCodes = REASON_CODES;
  readonly items = signal<DeadLetter[]>([]);
  readonly total = signal(0);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly pageSize = signal(50);

  readonly selection = new SelectionModel<DeadLetter>(true, []);

  readonly filters = this.fb.group({
    tenantId: [''],
    collectorId: [''],
    reasonCode: [''],
    status: [''],
  });

  readonly columns = ['select', 'reasonCode', 'status', 'collectorName', 'tenantId', 'receivedAt', 'actions'];

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.selection.clear();
    const f = this.filters.getRawValue();
    const activeFilters: DeadLetterFilters = {};
    if (f.tenantId) activeFilters.tenantId = f.tenantId;
    if (f.collectorId) activeFilters.collectorId = f.collectorId;
    if (f.reasonCode) activeFilters.reasonCode = f.reasonCode;
    if (f.status) activeFilters.status = f.status;
    this.svc.getDeadLetters(activeFilters, this.page(), this.pageSize()).subscribe({
      next: (r) => { this.items.set(r.items); this.total.set(r.total); this.loading.set(false); },
      error: () => { this.error.set('Failed to load dead letters.'); this.loading.set(false); },
    });
  }

  onPage(e: PageEvent): void {
    this.page.set(e.pageIndex + 1);
    this.pageSize.set(e.pageSize);
    this.load();
  }

  toggleAll(): void {
    if (this.allSelected()) this.selection.clear();
    else this.items().forEach((r) => this.selection.select(r));
  }

  allSelected(): boolean {
    return this.selection.selected.length === this.items().length && this.items().length > 0;
  }

  replaySelected(): void {
    const ids = this.selection.selected.map((r) => r.id);
    if (!ids.length) return;
    this.svc.replayById(ids).subscribe({ next: () => this.load() });
  }

  discardSelected(): void {
    const ids = this.selection.selected.map((r) => r.id);
    if (!ids.length) return;
    this.svc.discard(ids).subscribe({ next: () => this.load() });
  }

  replayAll(): void {
    const f = this.filters.getRawValue();
    const filter: DeadLetterFilters = {};
    if (f.tenantId) filter.tenantId = f.tenantId;
    if (f.collectorId) filter.collectorId = f.collectorId;
    if (f.reasonCode) filter.reasonCode = f.reasonCode;
    if (f.status) filter.status = f.status;
    this.svc.replayByFilter(filter).subscribe({ next: () => this.load() });
  }

  openDetail(row: DeadLetter): void {
    this.router.navigate(['/dead-letters', row.id]);
  }
}
