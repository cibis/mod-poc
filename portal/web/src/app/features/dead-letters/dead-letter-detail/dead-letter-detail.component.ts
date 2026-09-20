import { Component, OnInit, inject, signal, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { DeadLetterService } from '../../../core/api/dead-letter.service';
import { DeadLetter } from '../../../core/api/models';

@Component({
  selector: 'app-dead-letter-detail',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './dead-letter-detail.component.html',
})
export class DeadLetterDetailComponent implements OnInit {
  readonly id = input.required<string>();

  private readonly svc = inject(DeadLetterService);
  private readonly router = inject(Router);

  readonly item = signal<DeadLetter | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.svc.getDeadLetter(this.id()).subscribe({
      next: (d) => { this.item.set(d); this.loading.set(false); },
      error: () => { this.error.set('Failed to load dead letter.'); this.loading.set(false); },
    });
  }

  formattedJson(): string {
    const raw = this.item()?.eventJson;
    if (!raw) return '';
    try { return JSON.stringify(JSON.parse(raw), null, 2); } catch { return raw; }
  }

  replay(): void {
    const id = this.item()?.id;
    if (!id) return;
    this.svc.replayById([id]).subscribe({ next: () => this.router.navigate(['/dead-letters']) });
  }

  discard(): void {
    const id = this.item()?.id;
    if (!id) return;
    this.svc.discard([id]).subscribe({ next: () => this.router.navigate(['/dead-letters']) });
  }
}
