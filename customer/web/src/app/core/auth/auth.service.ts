import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { tap } from 'rxjs/operators';
import { Observable } from 'rxjs';

interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  displayName: string;
  kind: string;
  tenantId: string | null;
  tenantName: string | null;
}

interface TokenPayload {
  sub: string;
  name: string;
  kind: string;
  tenant_id?: string;
  exp: number;
}

const TOKEN_KEY = 'mod_access_token';
const TENANT_NAME_KEY = 'mod_tenant_name';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly _token = signal<string | null>(sessionStorage.getItem(TOKEN_KEY));
  readonly tenantName = signal<string | null>(sessionStorage.getItem(TENANT_NAME_KEY));

  readonly token = this._token.asReadonly();

  readonly isAuthenticated = computed(() => {
    const t = this._token();
    if (!t) return false;
    try {
      const p = this.decodePayload(t);
      return p.exp * 1000 > Date.now();
    } catch {
      return false;
    }
  });

  readonly displayName = computed(() => {
    const t = this._token();
    if (!t) return null;
    try { return this.decodePayload(t).name; } catch { return null; }
  });

  readonly tenantId = computed(() => {
    const t = this._token();
    if (!t) return null;
    try { return this.decodePayload(t).tenant_id ?? null; } catch { return null; }
  });

  login(userName: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/login', { userName, password }).pipe(
      tap(r => {
        sessionStorage.setItem(TOKEN_KEY, r.accessToken);
        this._token.set(r.accessToken);
        if (r.tenantName) {
          sessionStorage.setItem(TENANT_NAME_KEY, r.tenantName);
          this.tenantName.set(r.tenantName);
        }
      }),
    );
  }

  logout(): void {
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(TENANT_NAME_KEY);
    this._token.set(null);
    this.tenantName.set(null);
    this.router.navigate(['/login']);
  }

  private decodePayload(token: string): TokenPayload {
    const parts = token.split('.');
    if (parts.length !== 3) throw new Error('Invalid token');
    const b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    return JSON.parse(atob(b64)) as TokenPayload;
  }
}
