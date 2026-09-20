import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PagedResult, DeadLetter, BulkReplayResult } from './models';

export interface DeadLetterFilters {
  tenantId?: string;
  collectorId?: string;
  reasonCode?: string;
  status?: string;
}

@Injectable({ providedIn: 'root' })
export class DeadLetterService {
  private readonly http = inject(HttpClient);

  getDeadLetters(filters?: DeadLetterFilters, page = 1, pageSize = 50): Observable<PagedResult<DeadLetter>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (filters?.tenantId) params = params.set('tenantId', filters.tenantId);
    if (filters?.collectorId) params = params.set('collectorId', filters.collectorId);
    if (filters?.reasonCode) params = params.set('reasonCode', filters.reasonCode);
    if (filters?.status) params = params.set('status', filters.status);
    return this.http.get<PagedResult<DeadLetter>>('/api/admin/dead-letters', { params });
  }

  getDeadLetter(id: string): Observable<DeadLetter> {
    return this.http.get<DeadLetter>(`/api/admin/dead-letters/${id}`);
  }

  replayById(ids: string[]): Observable<BulkReplayResult> {
    return this.http.post<BulkReplayResult>('/api/admin/dead-letters/replay', { ids });
  }

  replayByFilter(filter: DeadLetterFilters): Observable<BulkReplayResult> {
    return this.http.post<BulkReplayResult>('/api/admin/dead-letters/replay', { filter });
  }

  discard(ids: string[]): Observable<BulkReplayResult> {
    return this.http.post<BulkReplayResult>('/api/admin/dead-letters/discard', { ids });
  }
}
