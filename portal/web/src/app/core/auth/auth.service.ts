import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { tap } from 'rxjs/operators';

export interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  displayName: string;
  kind: string;
  tenantId: string | null;
  tenantName: string | null;
}

export interface AuthUser {
  sub: string;
  name: string;
  kind: string;
}

const TOKEN_KEY = 'mod_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly token = signal<string | null>(sessionStorage.getItem(TOKEN_KEY));

  readonly user = computed<AuthUser | null>(() => {
    const t = this.token();
    if (!t) return null;
    try {
      const payload = JSON.parse(atob(t.split('.')[1]));
      return { sub: payload.sub, name: payload.name, kind: payload.kind };
    } catch {
      return null;
    }
  });

  readonly isLoggedIn = computed(() => !!this.token());

  login(userName: string, password: string) {
    return this.http
      .post<LoginResponse>('/api/auth/login', { userName, password })
      .pipe(
        tap((res) => {
          this.token.set(res.accessToken);
          sessionStorage.setItem(TOKEN_KEY, res.accessToken);
        }),
      );
  }

  logout(): void {
    this.token.set(null);
    sessionStorage.removeItem(TOKEN_KEY);
    this.router.navigate(['/login']);
  }
}
