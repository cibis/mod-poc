import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { LoginComponent } from './core/auth/login/login.component';
import { ShellComponent } from './core/layout/shell/shell.component';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      {
        path: 'tenants',
        loadChildren: () => import('./features/tenants/tenants.routes'),
      },
      {
        path: 'collectors',
        loadChildren: () => import('./features/collectors/collectors.routes'),
      },
      {
        path: 'dead-letters',
        loadChildren: () => import('./features/dead-letters/dead-letters.routes'),
      },
      {
        path: 'restatements',
        loadChildren: () => import('./features/restatements/restatements.routes'),
      },
      {
        path: 'audit',
        loadChildren: () => import('./features/audit/audit.routes'),
      },
      {
        path: 'simulation',
        loadChildren: () =>
          import('./simulation/simulation.routes').then((m) => m.SIMULATION_ROUTES),
      },
      { path: '', redirectTo: 'tenants', pathMatch: 'full' },
    ],
  },
  { path: '**', redirectTo: '' },
];
