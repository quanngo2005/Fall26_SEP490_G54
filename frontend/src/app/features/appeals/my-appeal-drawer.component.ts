import { Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AppealDetail, AppealEvidence, AppealListItem } from '../../core/appeal.models';
import { AppealService } from '../../core/appeal.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { displayBytes } from '../../shared/ui/display';
import { AppDatePipe } from '../../shared/pipes';
import { appealCode, appealSubjectWithScore, openBlob, typeKey } from './appeal-display';

@Component({
  selector: 'app-my-appeal-drawer',
  standalone: true,
  imports: [AppDatePipe],
  templateUrl: './my-appeal-drawer.component.html',
  styleUrls: ['./my-appeal-drawer.component.scss'],
  host: {
    '(window:keydown)': 'onKeydown($event)',
  },
})
export class MyAppealDrawerComponent extends LocalizedComponent {
  private readonly api = inject(AppealService);
  private readonly destroyRef = inject(DestroyRef);

  readonly appeal = input.required<AppealListItem>();
  readonly detail = input<AppealDetail | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);
  readonly closeDrawer = output<void>();
  readonly withdraw = output<AppealListItem>();
  readonly selectNext = output<void>();
  readonly selectPrev = output<void>();

  protected readonly previewFailed = signal(false);

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.closeDrawer.emit();
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.selectPrev.emit();
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.selectNext.emit();
    }
  }

  protected code(): string {
    return appealCode(this.appeal().id);
  }

  protected kindLabel(): string {
    return this.localization.appeals[typeKey(this.appeal().type)];
  }

  protected subject(): string {
    return (
      appealSubjectWithScore(this.appeal(), (score) => this.i18n.formatNumber(score)) ??
      this.localization.appeals.notAvailable
    );
  }

  protected subtitle(): string {
    const appeal = this.appeal();
    return this.i18n.translate('appeals.filedOn', {
      time: this.i18n.formatDate(appeal.filedAt, 'DD/MM/YYYY HH:mm'),
      handler: appeal.resolvedByEmployeeName ?? this.localization.appeals.notAvailable,
    });
  }

  protected size(bytes: number | null): string {
    return displayBytes(bytes, this.i18n.currentLanguage(), this.localization.appeals.notAvailable);
  }

  protected confirmWithdraw(): void {
    if (window.confirm(this.localization.appeals.withdrawConfirm)) {
      this.withdraw.emit(this.appeal());
    }
  }

  protected preview(file: AppealEvidence): void {
    this.previewFailed.set(false);
    this.api
      .evidence(file.previewUrl)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => openBlob(blob),
        error: () => this.previewFailed.set(true),
      });
  }
}
