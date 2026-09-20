import { Routes } from '@angular/router';
import { CollectorListComponent } from './collector-list/collector-list.component';
import { CollectorDetailComponent } from './collector-detail/collector-detail.component';

export default [
  { path: '', component: CollectorListComponent },
  { path: ':collectorId', component: CollectorDetailComponent },
] satisfies Routes;
