import { Injectable, inject, signal, OnDestroy } from '@angular/core';
import { Subject } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { AuthService } from '../auth/auth.service';
import { SiteStatus, AssetRollup } from '../api/api.types';

export interface RollupsUpdatedEvent {
  items: (AssetRollup & { assetId: string })[];
}

export interface FreshnessUpdatedEvent {
  sites: SiteStatus[];
}

@Injectable({ providedIn: 'root' })
export class LiveHubService implements OnDestroy {
  private readonly auth = inject(AuthService);

  private connection: signalR.HubConnection | null = null;

  readonly connected = signal(false);
  readonly rollupsUpdated$ = new Subject<RollupsUpdatedEvent>();
  readonly freshnessUpdated$ = new Subject<FreshnessUpdatedEvent>();

  start(): void {
    if (this.connection) return;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/live', {
        accessTokenFactory: () => this.auth.token() ?? '',
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('rollupsUpdated', (data: RollupsUpdatedEvent) =>
      this.rollupsUpdated$.next(data),
    );
    this.connection.on('freshnessUpdated', (data: FreshnessUpdatedEvent) =>
      this.freshnessUpdated$.next(data),
    );

    this.connection.onclose(() => this.connected.set(false));
    this.connection.onreconnecting(() => this.connected.set(false));
    this.connection.onreconnected(() => this.connected.set(true));

    this.connection
      .start()
      .then(() => this.connected.set(true))
      .catch(err => console.error('SignalR error:', err));
  }

  stop(): void {
    this.connection?.stop();
    this.connection = null;
    this.connected.set(false);
  }

  ngOnDestroy(): void {
    this.stop();
  }
}
