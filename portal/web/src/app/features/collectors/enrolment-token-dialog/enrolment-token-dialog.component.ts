import { Component, inject, signal, OnInit } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { CollectorService } from '../../../core/api/collector.service';

@Component({
  selector: 'app-enrolment-token-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatProgressSpinnerModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>Enrolment Token</h2>
    <mat-dialog-content>
      @if (loading()) {
        <mat-spinner diameter="32" />
      } @else if (error()) {
        <p class="error-msg">{{ error() }}</p>
      } @else if (token()) {
        <p class="warn-msg">This token is shown once and cannot be retrieved again.</p>
        <div class="token-box">
          <code>{{ token() }}</code>
          <button mat-icon-button (click)="copy()" title="Copy"><mat-icon>content_copy</mat-icon></button>
        </div>
        <p class="expiry">Expires: {{ expiry() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Close</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .token-box { display: flex; align-items: center; gap: 8px; background: #f5f5f5; padding: 8px 12px; border-radius: 4px; word-break: break-all; }
    .warn-msg { color: #e65100; }
    .expiry { font-size: 0.85rem; color: #666; }
  `],
})
export class EnrolmentTokenDialogComponent implements OnInit {
  private readonly svc = inject(CollectorService);
  private readonly data = inject<{ collectorId: string }>(MAT_DIALOG_DATA);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly token = signal<string | null>(null);
  readonly expiry = signal<string | null>(null);

  ngOnInit(): void {
    this.svc.createEnrolmentToken(this.data.collectorId).subscribe({
      next: (r) => { this.token.set(r.token); this.expiry.set(r.expiresAt); this.loading.set(false); },
      error: () => { this.error.set('Failed to generate token.'); this.loading.set(false); },
    });
  }

  copy(): void {
    const t = this.token();
    if (t) navigator.clipboard.writeText(t);
  }
}
