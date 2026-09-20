// DEMO SCAFFOLDING
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { SimStoreService } from '../../services/sim-store.service';
import { SimApiService } from '../../services/sim-api.service';
import { Scenario, ScenarioRun } from '../../models';

@Component({
  selector: 'app-scenarios',
  standalone: true,
  imports: [
    ReactiveFormsModule, MatCardModule, MatButtonModule, MatFormFieldModule,
    MatInputModule, MatProgressSpinnerModule, MatTableModule, MatIconModule, MatTooltipModule,
  ],
  templateUrl: './scenarios.component.html',
  styleUrl: './scenarios.component.scss',
})
export class ScenariosComponent implements OnInit {
  readonly store = inject(SimStoreService);
  private readonly api = inject(SimApiService);
  private readonly fb = inject(FormBuilder);

  readonly scenarios = signal<Scenario[]>([]);
  readonly scenariosLoading = signal(true);

  readonly activeRun = computed(() =>
    this.store.scenarioRuns().find((r) => r.status === 'running') ?? null,
  );

  readonly historyColumns = ['name', 'status', 'startedAt', 'endedAt', 'duration'];

  readonly paramForms = signal<Map<string, ReturnType<typeof this.fb.group>>>(new Map());

  readonly runError = signal<string | null>(null);
  readonly runBusy = signal(false);

  ngOnInit(): void {
    this.api.getScenarios().subscribe({
      next: (s) => {
        this.scenarios.set(s);
        this.scenariosLoading.set(false);
        const map = new Map<string, ReturnType<typeof this.fb.group>>();
        for (const sc of s) {
          const controls = Object.fromEntries(
            sc.parameters.map((p) => [p.name, [p.default ?? '']]),
          );
          map.set(sc.name, this.fb.group(controls));
        }
        this.paramForms.set(map);
      },
      error: () => this.scenariosLoading.set(false),
    });
  }

  formFor(name: string) {
    return this.paramForms().get(name);
  }

  runScenario(sc: Scenario): void {
    if (this.activeRun()) return;
    this.runBusy.set(true);
    this.runError.set(null);
    const params = this.formFor(sc.name)?.getRawValue() ?? {};
    this.api.runScenario(sc.name, params as Record<string, unknown>).subscribe({
      next: () => this.runBusy.set(false),
      error: (e) => { this.runBusy.set(false); this.runError.set(e.error?.title ?? 'Failed to start scenario.'); },
    });
  }

  stopRun(): void {
    const r = this.activeRun();
    if (!r) return;
    this.api.stopScenarioRun(r.scenarioRunId).subscribe();
  }

  elapsed(r: ScenarioRun): string {
    const end = r.endedAt ? new Date(r.endedAt) : new Date();
    const ms = end.getTime() - new Date(r.startedAt).getTime();
    const s = Math.floor(ms / 1000);
    if (s < 60) return `${s}s`;
    return `${Math.floor(s / 60)}m ${s % 60}s`;
  }

  formatDate(d: string | null): string {
    if (!d) return '—';
    return new Date(d).toLocaleString();
  }

  historyRuns = computed(() =>
    this.store.scenarioRuns().filter((r) => r.status !== 'running'),
  );
}
