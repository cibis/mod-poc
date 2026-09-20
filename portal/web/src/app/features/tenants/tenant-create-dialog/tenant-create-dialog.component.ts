import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TenantService } from '../../../core/api/tenant.service';

@Component({
  selector: 'app-tenant-create-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './tenant-create-dialog.component.html',
})
export class TenantCreateDialogComponent {
  private readonly svc = inject(TenantService);
  private readonly dialogRef = inject(MatDialogRef<TenantCreateDialogComponent>);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    isolationMode: ['Pooled', Validators.required],
  });

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  submit(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    this.error.set(null);
    this.svc.createTenant(this.form.getRawValue()).subscribe({
      next: (t) => this.dialogRef.close(t),
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.title ?? 'Failed to create tenant.');
      },
    });
  }
}
