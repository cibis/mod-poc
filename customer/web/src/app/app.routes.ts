import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: '', redirectTo: 'sites', pathMatch: 'full' },
  {
    path: 'login',
    loadComponent: () => import('./pages/login/login.component').then(m => m.LoginComponent),
  },
  {
    path: 'sites',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/sites/sites.component').then(m => m.SitesComponent),
  },
  {
    path: 'sites/:siteId',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/site/site.component').then(m => m.SiteComponent),
  },
  {
    path: 'assets/:assetId',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/asset/asset.component').then(m => m.AssetComponent),
  },
  { path: '**', redirectTo: 'sites' },
];
