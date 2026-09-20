// DEMO SCAFFOLDING
import { Injectable, OnDestroy, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { AuthService } from '../../core/auth/auth.service';
import { SimStoreService } from './sim-store.service';

@Injectable({ providedIn: 'root' })
export class SimHubService implements OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly store = inject(SimStoreService);

  private connection: signalR.HubConnection | null = null;

  start(): void {
    if (this.connection) return;
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/sim', {
        accessTokenFactory: () => this.auth.token() ?? '',
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('metricsTick', (tick) => this.store.applyMetricsTick(tick));
    this.connection.on('collectorStatus', (ev) => this.store.applyCollectorStatus(ev));
    this.connection.on('provisioning', (ev) => this.store.applyProvisioning(ev));
    this.connection.on('topologyChanged', (snap) => this.store.applyTopology(snap));
    this.connection.on('timelineMarker', (m) => this.store.applyTimelineMarker(m));
    this.connection.on('scenarioRun', (run) => this.store.applyScenarioRun(run));

    this.connection.onreconnected(() => {
      this.store.setReconnected();
      this.store.reload();
    });

    this.connection.onclose(() => this.store.setHubState('disconnected'));
    this.connection.onreconnecting(() => this.store.setHubState('reconnecting'));

    this.store.setHubState('connecting');
    this.connection
      .start()
      .then(() => this.store.setHubState('connected'))
      .catch(() => this.store.setHubState('disconnected'));
  }

  stop(): void {
    this.connection?.stop();
    this.connection = null;
  }

  ngOnDestroy(): void {
    this.stop();
  }
}
