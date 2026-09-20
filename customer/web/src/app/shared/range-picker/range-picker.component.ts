import { Component, input, output } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { FormsModule } from '@angular/forms';

export type RangeMinutes = 15 | 60 | 360 | 1440;

export const RANGE_OPTIONS: { label: string; value: RangeMinutes }[] = [
  { label: '15 min', value: 15 },
  { label: '1 h', value: 60 },
  { label: '6 h', value: 360 },
  { label: '24 h', value: 1440 },
];

@Component({
  selector: 'app-range-picker',
  standalone: true,
  imports: [MatButtonToggleModule, FormsModule],
  templateUrl: './range-picker.component.html',
  styleUrl: './range-picker.component.scss',
})
export class RangePickerComponent {
  readonly value = input<RangeMinutes>(60);
  readonly changed = output<RangeMinutes>();

  readonly options = RANGE_OPTIONS;

  onchange(v: RangeMinutes): void {
    this.changed.emit(v);
  }
}
