import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  PagedResult,
  CollectorSummary,
  CollectorDetail,
  EnrolmentToken,
  CommandResult,
  HealthHistory,
  CollectorMapping,
} from './models';

export interface CollectorFilters {
  tenantId?: string;
  siteId?: string;
  status?: string;
}

@Injectable({ providedIn: 'root' })
export class CollectorService {
  private readonly http = inject(HttpClient);

  getCollectors(filters?: CollectorFilters, page = 1, pageSize = 50): Observable<PagedResult<CollectorSummary>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (filters?.tenantId) params = params.set('tenantId', filters.tenantId);
    if (filters?.siteId) params = params.set('siteId', filters.siteId);
    if (filters?.status) params = params.set('status', filters.status);
    return this.http.get<PagedResult<CollectorSummary>>('/api/admin/collectors', { params });
  }

  getCollector(id: string): Observable<CollectorDetail> {
    return this.http.get<CollectorDetail>(`/api/admin/collectors/${id}`);
  }

  registerCollector(
    siteId: string,
    body: { name: string; commandChannelEnabled: boolean },
  ): Observable<CollectorSummary> {
    return this.http.post<CollectorSummary>(`/api/admin/sites/${siteId}/collectors`, body);
  }

  updateConfig(id: string, body: Record<string, unknown>): Observable<CollectorDetail> {
    return this.http.put<CollectorDetail>(`/api/admin/collectors/${id}/config`, body);
  }

  updateMappings(id: string, mappings: Array<{ sourceId: string; assetId: string }>): Observable<CollectorDetail> {
    return this.http.put<CollectorDetail>(`/api/admin/collectors/${id}/mappings`, mappings);
  }

  createEnrolmentToken(id: string): Observable<EnrolmentToken> {
    return this.http.post<EnrolmentToken>(`/api/admin/collectors/${id}/enrolment-tokens`, {});
  }

  revokeCollector(id: string, reason: string): Observable<void> {
    return this.http.post<void>(`/api/admin/collectors/${id}/revoke`, { reason });
  }

  sendCommand(id: string, type: string): Observable<CommandResult> {
    return this.http.post<CommandResult>(`/api/admin/collectors/${id}/commands`, { type });
  }

  getHealthHistory(id: string, from: string, to: string): Observable<HealthHistory[]> {
    return this.http.get<HealthHistory[]>(`/api/admin/collectors/${id}/health-history`, {
      params: { from, to },
    });
  }

  getMappings(id: string): Observable<CollectorMapping[]> {
    return this.http.get<CollectorMapping[]>(`/api/admin/collectors/${id}/mappings`);
  }
}
