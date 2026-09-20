import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { CollectorService } from '../../../core/api/collector.service';

@Component({
  selector: 'app-collector-revoke-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Revoke Collector</h2>
    <mat-dialog-content>
      <p>This will revoke all certificates and delete command queues.</p>
      <form [formGroup]="form">
        <mat-form-field class="full-width"><mat-label>Reason</mat-label><input matInput formControlName="reason" /></mat-form-field>
      </form>
      @if (error()) { <p class="error-msg">{{ error() }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button color="warn" (click)="submit()" [disabled]="loading() || form.invalid">
        @if (loading()) { Revoking… } @else { Revoke }
      </button>
    </mat-dialog-actions>
  `,
})
export class CollectorRevokeDialogComponent {
  private readonly svc = inject(CollectorService);
  private readonly dialogRef = inject(MatDialogRef<CollectorRevokeDialogComponent>);
  private readonly data = inject<{ collectorId: string }>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({ reason: ['', Validators.required] });
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  submit(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    this.svc.revokeCollector(this.data.collectorId, this.form.getRawValue().reason).subscribe({
      next: () => this.dialogRef.close(true),
      error: (err) => { this.loading.set(false); this.error.set(err.error?.title ?? 'Failed to revoke.'); },
    });
  }
}
