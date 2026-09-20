// DEMO SCAFFOLDING
import { Component, Inject, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { TargetSelector } from '../../models';
import { SimApiService } from '../../services/sim-api.service';

export interface CommandDialogData {
  title: string;
  commandType: string;
  fields: Array<{ name: string; label: string; type: 'number' | 'text' | 'checkbox'; default?: unknown }>;
  targets: TargetSelector;
}

@Component({
  selector: 'app-set-command-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule,
    MatInputModule, MatButtonModule, MatCheckboxModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="cmd-form">
        @for (f of data.fields; track f.name) {
          @if (f.type === 'checkbox') {
            <mat-checkbox [formControlName]="f.name">{{ f.label }}</mat-checkbox>
          } @else {
            <mat-form-field>
              <mat-label>{{ f.label }}</mat-label>
              <input matInput [type]="f.type" [formControlName]="f.name" />
            </mat-form-field>
          }
        }
      </form>
      @if (error()) { <p class="error-msg">{{ error() }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button color="primary" (click)="submit()" [disabled]="loading()">
        @if (loading()) { Sending… } @else { Send }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .cmd-form { display: flex; flex-direction: column; gap: 4px; min-width: 300px; }
    .error-msg { color: #c62828; font-size: 0.85rem; }
  `],
})
export class SetCommandDialogComponent {
  readonly data = inject<CommandDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(SimApiService);
  private readonly dialogRef = inject(MatDialogRef<SetCommandDialogComponent>);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.group(
    Object.fromEntries(
      this.data.fields.map((f) => [f.name, [f.default ?? '', f.type === 'number' ? Validators.min(0) : []]]),
    ),
  );

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  submit(): void {
    this.loading.set(true);
    this.api.postCommand(this.data.targets, {
      type: this.data.commandType,
      args: this.form.getRawValue() as Record<string, unknown>,
    }).subscribe({
      next: () => this.dialogRef.close(true),
      error: (e) => { this.loading.set(false); this.error.set(e.error?.title ?? 'Failed.'); },
    });
  }
}
