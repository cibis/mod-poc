import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-asset-form-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './asset-form-dialog.component.html',
})
export class AssetFormDialogComponent {
  private readonly dialogRef = inject(MatDialogRef<AssetFormDialogComponent>);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    assetType: ['', Validators.required],
    processValueName: ['', Validators.required],
    processValueUnit: ['', Validators.required],
    processValueMin: [null as number | null],
    processValueMax: [null as number | null],
  });

  submit(): void {
    if (this.form.valid) this.dialogRef.close(this.form.getRawValue());
  }
}
