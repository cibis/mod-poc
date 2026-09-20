import { Component, input } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Freshness } from '../../core/api/api.types';
import { RelativeTimePipe } from '../pipes/relative-time.pipe';

@Component({
  selector: 'app-freshness-badge',
  standalone: true,
  imports: [MatTooltipModule, RelativeTimePipe],
  templateUrl: './freshness-badge.component.html',
  styleUrl: './freshness-badge.component.scss',
})
export class FreshnessBadgeComponent {
  readonly freshness = input.required<Freshness>();
}
