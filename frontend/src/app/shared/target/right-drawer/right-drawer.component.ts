import { Component, computed, input, output } from '@angular/core';
import { LocalizedComponent } from '../../../core/i18n/localized.component';

/**
 * 560px right-hand drawer for the target allocation v3 screens: 64px header, 72px footer, no backdrop (the
 * host page stays interactive/visible beside it). Body and footer content are projected; the header title
 * is a plain input since it is usually a short, dynamic label (KPI/department name) rather than static copy.
 */
@Component({
  selector: 'app-right-drawer',
  standalone: true,
  templateUrl: './right-drawer.component.html',
  styleUrl: './right-drawer.component.scss',
})
export class RightDrawerComponent extends LocalizedComponent {
  readonly open = input(false);
  readonly titleText = input('');
  readonly subtitleText = input<string | null>(null);

  readonly closed = output<void>();

  protected readonly text = computed(() => this.localization.targetShared.rightDrawer);

  protected close(): void {
    this.closed.emit();
  }
}
