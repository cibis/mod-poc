import { Routes } from '@angular/router';
import { TenantListComponent } from './tenant-list/tenant-list.component';
import { TenantDetailComponent } from './tenant-detail/tenant-detail.component';

export default [
  { path: '', component: TenantListComponent },
  { path: ':tenantId', component: TenantDetailComponent },
] satisfies Routes;
