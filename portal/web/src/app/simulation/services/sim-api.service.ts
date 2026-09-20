// DEMO SCAFFOLDING
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  SimCollector, TopologySnapshot, MetricsResponse, TimelineMarker,
  Scenario, ScenarioRun, PowerResponse, CommandResponse,
  GenerateFleetRequest, TargetSelector,
} from '../models';

@Injectable({ providedIn: 'root' })
export class SimApiService {
  private readonly http = inject(HttpClient);

  getCollectors(): Observable<SimCollector[]> {
    return this.http.get<SimCollector[]>('/api/sim/collectors');
  }

  getTopology(): Observable<TopologySnapshot> {
    return this.http.get<TopologySnapshot>('/api/sim/topology');
  }

  getMetrics(keys: string[], from: Date, to: Date): Observable<MetricsResponse> {
    const params = new HttpParams()
      .set('keys', keys.join(','))
      .set('from', from.toISOString())
      .set('to', to.toISOString());
    return this.http.get<MetricsResponse>('/api/sim/metrics', { params });
  }

  getTimeline(from: Date, to: Date): Observable<TimelineMarker[]> {
    const params = new HttpParams()
      .set('from', from.toISOString())
      .set('to', to.toISOString());
    return this.http.get<TimelineMarker[]>('/api/sim/timeline', { params });
  }

  getScenarios(): Observable<Scenario[]> {
    return this.http.get<Scenario[]>('/api/sim/scenarios');
  }

  getScenarioRuns(limit = 20): Observable<ScenarioRun[]> {
    return this.http.get<ScenarioRun[]>('/api/sim/scenario-runs', {
      params: new HttpParams().set('limit', limit),
    });
  }

  postPower(targets: TargetSelector, on: boolean): Observable<PowerResponse> {
    return this.http.post<PowerResponse>('/api/sim/power', { targets, on });
  }

  postCommand(
    targets: TargetSelector,
    command: { type: string; args: Record<string, unknown> },
  ): Observable<CommandResponse> {
    return this.http.post<CommandResponse>('/api/sim/commands', { targets, command });
  }

  generateFleet(req: GenerateFleetRequest): Observable<{ collectorIds: string[] }> {
    return this.http.post<{ collectorIds: string[] }>('/api/sim/fleet/generate', req);
  }

  runScenario(name: string, parameters: Record<string, unknown>): Observable<{ scenarioRunId: string }> {
    return this.http.post<{ scenarioRunId: string }>(`/api/sim/scenarios/${encodeURIComponent(name)}/run`, { parameters });
  }

  stopScenarioRun(id: string): Observable<void> {
    return this.http.post<void>(`/api/sim/scenario-runs/${encodeURIComponent(id)}/stop`, {});
  }
}
