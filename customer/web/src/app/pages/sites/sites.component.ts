import { Component, inject, signal, OnInit, OnDestroy, DestroyRef } from '@angular/core';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { ApiService } from '../../core/api/api.service';
import { LiveHubService } from '../../core/live/live-hub.service';
import { SiteStatus } from '../../core/api/api.types';
import { FreshnessBadgeComponent } from '../../shared/freshness-badge/freshness-badge.component';

@Component({
  selector: 'app-sites',
  standalone: true,
  imports: [
    RouterLink,
    MatCardModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    FreshnessBadgeComponent,
  ],
  templateUrl: './sites.component.html',
  styleUrl: './sites.component.scss',
})
export class SitesComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly liveHub = inject(LiveHubService);
  private readonly destroyRef = inject(DestroyRef);

  readonly sites = signal<SiteStatus[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  private freshnessSubscription?: ReturnType<typeof this.liveHub.freshnessUpdated$.subscribe>;

  ngOnInit(): void {
    this.api
      .getSitesStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: s => {
          this.sites.set(s);
          this.loading.set(false);
        },
        error: err => {
          this.error.set((err as { status?: number }).status === 404 ? 'Not found' : 'Failed to load sites');
          this.loading.set(false);
        },
      });

    this.liveHub.start();
    this.freshnessSubscription = this.liveHub.freshnessUpdated$.subscribe(e =>
      this.sites.set(e.sites),
    );
  }

  ngOnDestroy(): void {
    this.freshnessSubscription?.unsubscribe();
  }
}
