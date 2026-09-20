// DEMO SCAFFOLDING
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { SimApiService } from '../../services/sim-api.service';

@Component({
  selector: 'app-fleet-generator-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule,
    MatInputModule, MatButtonModule, MatSlideToggleModule,
  ],
  template: `
    <h2 mat-dialog-title>Generate Fleet</h2>
    <mat-dialog-content>
      <p class="note">Uses the ordinary admin registration path — same code path as manual creation.</p>
      <form [formGroup]="form" class="gen-form">
        <mat-form-field>
          <mat-label>Tenant ID</mat-label>
          <input matInput formControlName="tenantId" placeholder="GUID" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>Sites</mat-label>
          <input matInput type="number" formControlName="sites" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>Lines per site</mat-label>
          <input matInput type="number" formControlName="linesPerSite" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>Assets per line</mat-label>
          <input matInput type="number" formControlName="assetsPerLine" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>Region label</mat-label>
          <input matInput formControlName="regionLabel" />
        </mat-form-field>
        <mat-slide-toggle formControlName="commandChannelEnabled">Enable command channel</mat-slide-toggle>
      </form>
      @if (error()) { <p class="error-msg">{{ error() }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button color="warn" (click)="submit()" [disabled]="loading() || form.invalid">
        @if (loading()) { Generating… } @else { Generate }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .gen-form { display: flex; flex-direction: column; gap: 4px; min-width: 340px; }
    .note { font-size: 0.8rem; color: #666; margin-top: 0; }
    .error-msg { color: #c62828; font-size: 0.85rem; }
  `],
})
export class FleetGeneratorDialogComponent {
  private readonly api = inject(SimApiService);
  private readonly dialogRef = inject(MatDialogRef<FleetGeneratorDialogComponent>);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    tenantId: ['', Validators.required],
    sites: [1, [Validators.required, Validators.min(1)]],
    linesPerSite: [2, [Validators.required, Validators.min(1)]],
    assetsPerLine: [3, [Validators.required, Validators.min(1)]],
    regionLabel: ['region-a', Validators.required],
    commandChannelEnabled: [false],
  });

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  submit(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    this.api.generateFleet(this.form.getRawValue()).subscribe({
      next: (r) => this.dialogRef.close(r),
      error: (e) => { this.loading.set(false); this.error.set(e.error?.title ?? 'Failed.'); },
    });
  }
}
