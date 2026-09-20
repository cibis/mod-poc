import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { CollectorService } from '../../../core/api/collector.service';

@Component({
  selector: 'app-collector-register-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatSlideToggleModule,
  ],
  template: `
    <h2 mat-dialog-title>Register Collector</h2>
    <mat-dialog-content>
      <form [formGroup]="form">
        <mat-form-field class="full-width"><mat-label>Site ID</mat-label><input matInput formControlName="siteId" /></mat-form-field>
        <mat-form-field class="full-width"><mat-label>Collector Name</mat-label><input matInput formControlName="name" /></mat-form-field>
        <mat-slide-toggle formControlName="commandChannelEnabled">Enable Command Channel</mat-slide-toggle>
      </form>
      @if (error()) { <p class="error-msg">{{ error() }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button color="primary" (click)="submit()" [disabled]="loading() || form.invalid">
        @if (loading()) { Registering… } @else { Register }
      </button>
    </mat-dialog-actions>
  `,
})
export class CollectorRegisterDialogComponent {
  private readonly svc = inject(CollectorService);
  private readonly dialogRef = inject(MatDialogRef<CollectorRegisterDialogComponent>);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    siteId: ['', Validators.required],
    name: ['', Validators.required],
    commandChannelEnabled: [false],
  });

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  submit(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    const { siteId, name, commandChannelEnabled } = this.form.getRawValue();
    this.svc.registerCollector(siteId, { name, commandChannelEnabled }).subscribe({
      next: (c) => this.dialogRef.close(c),
      error: (err) => { this.loading.set(false); this.error.set(err.error?.title ?? 'Failed to register.'); },
    });
  }
}
