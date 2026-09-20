import { Component, inject, input, signal, computed, OnInit, OnDestroy, DestroyRef } from '@angular/core';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { switchMap, EMPTY, catchError } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api/api.service';
import { LiveHubService } from '../../core/live/live-hub.service';
import { SiteStatus, LineSummary, HierarchyResponse, SiteInfo } from '../../core/api/api.types';
import { FreshnessBadgeComponent } from '../../shared/freshness-badge/freshness-badge.component';
import { RangePickerComponent, RangeMinutes } from '../../shared/range-picker/range-picker.component';

@Component({
  selector: 'app-site',
  standalone: true,
  imports: [
    RouterLink,
    MatCardModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatIconModule,
    FreshnessBadgeComponent,
    RangePickerComponent,
  ],
  templateUrl: './site.component.html',
  styleUrl: './site.component.scss',
})
export class SiteComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly liveHub = inject(LiveHubService);
  private readonly destroyRef = inject(DestroyRef);

  readonly siteId = input.required<string>();

  readonly rangeMinutes = signal<RangeMinutes>(60);
  readonly hierarchy = signal<HierarchyResponse | null>(null);
  readonly siteInfo = signal<SiteInfo | null>(null);
  readonly freshness = signal<SiteStatus | null>(null);
  readonly summary = signal<LineSummary[]>([]);
  readonly loadingSummary = signal(true);
  readonly errorSummary = signal<string | null>(null);
  readonly loadingHierarchy = signal(true);

  readonly rangeDates = computed(() => {
    const m = this.rangeMinutes();
    const to = new Date();
    const from = new Date(to.getTime() - m * 60_000);
    return { from: from.toISOString(), to: to.toISOString() };
  });

  readonly summaryColumns = ['name', 'eventCount', 'counterDelta', 'faultEventCount', 'restatedMinutes'];

  private freshnessSubscription?: ReturnType<typeof this.liveHub.freshnessUpdated$.subscribe>;

  ngOnInit(): void {
    this.api
      .getHierarchy()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: h => {
          this.hierarchy.set(h);
          this.siteInfo.set(h.sites.find(s => s.siteId === this.siteId()) ?? null);
          this.loadingHierarchy.set(false);
        },
        error: () => this.loadingHierarchy.set(false),
      });

    toObservable(this.rangeDates)
      .pipe(
        switchMap(({ from, to }) =>
          this.api.getSiteSummary(this.siteId(), from, to).pipe(
            catchError(err => {
              const status = (err as { status?: number }).status;
              this.errorSummary.set(status === 404 ? 'Not found' : 'Failed to load summary');
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(s => {
        this.summary.set(s);
        this.loadingSummary.set(false);
        this.errorSummary.set(null);
      });

    this.liveHub.start();
    this.freshnessSubscription = this.liveHub.freshnessUpdated$.subscribe(e => {
      const site = e.sites.find(s => s.siteId === this.siteId());
      if (site) this.freshness.set(site);
    });
  }

  ngOnDestroy(): void {
    this.freshnessSubscription?.unsubscribe();
  }
}
