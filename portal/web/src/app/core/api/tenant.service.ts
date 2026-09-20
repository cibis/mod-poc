import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PagedResult, Tenant, TenantHierarchy, Site, Line, Asset } from './models';

@Injectable({ providedIn: 'root' })
export class TenantService {
  private readonly http = inject(HttpClient);

  getTenants(page = 1, pageSize = 50): Observable<PagedResult<Tenant>> {
    return this.http.get<PagedResult<Tenant>>('/api/admin/tenants', {
      params: { page, pageSize },
    });
  }

  createTenant(body: { name: string; isolationMode: string }): Observable<Tenant> {
    return this.http.post<Tenant>('/api/admin/tenants', body);
  }

  getHierarchy(tenantId: string): Observable<TenantHierarchy> {
    return this.http.get<TenantHierarchy>(`/api/admin/tenants/${tenantId}/hierarchy`);
  }

  createSite(
    tenantId: string,
    body: { name: string; regionLabel: string; timeZone: string },
  ): Observable<Site> {
    return this.http.post<Site>(`/api/admin/tenants/${tenantId}/sites`, body);
  }

  createLine(siteId: string, body: { name: string }): Observable<Line> {
    return this.http.post<Line>(`/api/admin/sites/${siteId}/lines`, body);
  }

  createAsset(
    lineId: string,
    body: {
      name: string;
      assetType: string;
      processValueName: string;
      processValueUnit: string;
      processValueMin: number | null;
      processValueMax: number | null;
    },
  ): Observable<Asset> {
    return this.http.post<Asset>(`/api/admin/lines/${lineId}/assets`, body);
  }
}
