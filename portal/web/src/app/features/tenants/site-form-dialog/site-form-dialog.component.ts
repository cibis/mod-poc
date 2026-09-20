import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-site-form-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Add Site</h2>
    <mat-dialog-content>
      <form [formGroup]="form">
        <mat-form-field class="full-width"><mat-label>Name</mat-label><input matInput formControlName="name" /></mat-form-field>
        <mat-form-field class="full-width"><mat-label>Region Label</mat-label><input matInput formControlName="regionLabel" /></mat-form-field>
        <mat-form-field class="full-width"><mat-label>Time Zone</mat-label><input matInput formControlName="timeZone" placeholder="Europe/London" /></mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button color="primary" (click)="submit()" [disabled]="form.invalid">Add</button>
    </mat-dialog-actions>
  `,
})
export class SiteFormDialogComponent {
  private readonly dialogRef = inject(MatDialogRef<SiteFormDialogComponent>);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    regionLabel: ['', Validators.required],
    timeZone: ['', Validators.required],
  });

  submit(): void {
    if (this.form.valid) this.dialogRef.close(this.form.getRawValue());
  }
}
