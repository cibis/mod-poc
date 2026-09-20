import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PagedResult, Restatement } from './models';

@Injectable({ providedIn: 'root' })
export class RestatementService {
  private readonly http = inject(HttpClient);

  getRestatements(tenantId?: string, from?: string, to?: string, page = 1, pageSize = 50): Observable<PagedResult<Restatement>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (tenantId) params = params.set('tenantId', tenantId);
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<PagedResult<Restatement>>('/api/admin/restatements', { params });
  }
}
