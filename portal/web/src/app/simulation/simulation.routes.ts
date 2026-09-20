// DEMO SCAFFOLDING
import { Routes } from '@angular/router';
import { SimLayoutComponent } from './sim-layout/sim-layout.component';
import { OverviewComponent } from './pages/overview/overview.component';
import { FleetComponent } from './pages/fleet/fleet.component';
import { BuffersComponent } from './pages/buffers/buffers.component';
import { ScenariosComponent } from './pages/scenarios/scenarios.component';

export const SIMULATION_ROUTES: Routes = [
  {
    path: '',
    component: SimLayoutComponent,
    children: [
      { path: '', component: OverviewComponent },
      { path: 'fleet', component: FleetComponent },
      { path: 'buffers', component: BuffersComponent },
      { path: 'scenarios', component: ScenariosComponent },
    ],
  },
];
