// DEMO SCAFFOLDING
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatTabsModule } from '@angular/material/tabs';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { toObservable } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, filter } from 'rxjs/operators';
import { SimStoreService } from '../services/sim-store.service';
import { SimHubService } from '../services/sim-hub.service';

@Component({
  selector: 'app-sim-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatTabsModule, MatIconModule],
  templateUrl: './sim-layout.component.html',
  styleUrl: './sim-layout.component.scss',
})
export class SimLayoutComponent implements OnInit, OnDestroy {
  readonly store = inject(SimStoreService);
  private readonly hub = inject(SimHubService);
  private readonly snack = inject(MatSnackBar);

  private reconnectedSub = toObservable(this.store.showReconnected)
    .pipe(filter(Boolean), distinctUntilChanged())
    .subscribe(() => this.snack.open('Reconnected to simulator hub', 'OK', { duration: 3000 }));

  ngOnInit(): void {
    this.store.loading.set(true);
    this.store.reload();
    this.hub.start();
  }

  ngOnDestroy(): void {
    this.hub.stop();
    this.reconnectedSub.unsubscribe();
  }
}
