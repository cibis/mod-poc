import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PagedResult, AuditEntry } from './models';

export interface AuditFilters {
  tenantId?: string;
  from?: string;
  to?: string;
  action?: string;
}

@Injectable({ providedIn: 'root' })
export class AuditService {
  private readonly http = inject(HttpClient);

  getAudit(filters?: AuditFilters, page = 1, pageSize = 50): Observable<PagedResult<AuditEntry>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (filters?.tenantId) params = params.set('tenantId', filters.tenantId);
    if (filters?.from) params = params.set('from', filters.from);
    if (filters?.to) params = params.set('to', filters.to);
    if (filters?.action) params = params.set('action', filters.action);
    return this.http.get<PagedResult<AuditEntry>>('/api/admin/audit', { params });
  }
}
