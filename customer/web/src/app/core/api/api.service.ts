import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { HierarchyResponse, SiteStatus, LineSummary, AssetRollup, MeResponse } from './api.types';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  getMe(): Observable<MeResponse> {
    return this.http.get<MeResponse>('/api/me');
  }

  getHierarchy(): Observable<HierarchyResponse> {
    return this.http.get<HierarchyResponse>('/api/hierarchy');
  }

  getSitesStatus(): Observable<SiteStatus[]> {
    return this.http.get<SiteStatus[]>('/api/sites/status');
  }

  getSiteSummary(siteId: string, from: string, to: string): Observable<LineSummary[]> {
    const params = new HttpParams().set('from', from).set('to', to);
    return this.http.get<LineSummary[]>(`/api/sites/${siteId}/summary`, { params });
  }

  getAssetRollups(assetId: string, from: string, to: string): Observable<AssetRollup[]> {
    const params = new HttpParams().set('from', from).set('to', to);
    return this.http.get<AssetRollup[]>(`/api/assets/${assetId}/rollups`, { params });
  }
}
