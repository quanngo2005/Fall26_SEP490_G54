import { Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  CheckedField,
  MySubmissionDetail,
  MySubmissionFieldCheck,
  MySubmissionListItem,
  MySubmissionVersion,
} from '../../core/my-submission.models';
import { MySubmissionService } from '../../core/my-submission.service';
import { SubmissionEvidence } from '../../core/result-submission.models';
import { AppDatePipe } from '../../shared/pipes';
import { displayBytes } from '../../shared/ui/display';
import { openBlob } from '../appeals/appeal-display';
import { badgeOf, hoursLeft, KNOWN_REASONS } from './my-submission-display';

@Component({
  selector: 'app-my-submission-drawer',
  standalone: true,
  imports: [AppDatePipe, RouterLink],
  templateUrl: './my-submission-drawer.component.html',
  styleUrls: [
    '../appeals/appeals-shared.scss',
    '../appeals/my-appeal-drawer.component.scss',
    '../appeals/my-appeal-drawer-history.scss',
    './my-submission-drawer.component.scss',
  ],
  host: {
    '(window:keydown)': 'onKeydown($event)',
  },
})
export class MySubmissionDrawerComponent extends LocalizedComponent {
  private readonly api = inject(MySubmissionService);
  private readonly destroyRef = inject(DestroyRef);

  readonly item = input.required<MySubmissionListItem>();
  readonly detail = input<MySubmissionDetail | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);
  readonly closeDrawer = output<void>();
  readonly selectNext = output<void>();
  readonly selectPrev = output<void>();
  readonly selectVersion = output<string>();
  readonly appeal = output<string>();

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

  protected title(): string {
    const d = this.detail();
    return this.i18n.translate('submissions.drawer.title', {
      code: d?.workCode ?? this.item().workCode,
      no: d?.submissionNo ?? this.item().submissionNo,
    });
  }

  protected subtitle(): string {
    return this.i18n.translate('submissions.drawer.submittedOn', {
      time: this.i18n.formatDate(
        this.detail()?.submittedAt ?? this.item().submittedAt,
        'DD/MM/YYYY HH:mm:ss',
      ),
    });
  }

  protected badge() {
    const d = this.detail();
    const source = d ?? this.item();
    return badgeOf(source.status, source.matchStatus);
  }

  protected badgeLabel(): string {
    return this.localization.submissions.badge[this.badge().key];
  }

  protected versionBadge(version: MySubmissionVersion) {
    const { key, tone } = badgeOf(version.status, version.matchStatus);
    return { tone, label: this.localization.submissions.badge[key] };
  }

  protected fieldLabel(field: CheckedField): string {
    return this.localization.submissions.field[field];
  }

  protected value(field: MySubmissionFieldCheck, value: string | null): string {
    if (value === null || value === '') return this.localization.submissions.drawer.notCompared;
    return field.field === 'AMOUNT' ? this.i18n.formatNumber(value) : value;
  }

  protected hasImport(fields: readonly MySubmissionFieldCheck[]): boolean {
    return fields.some((f) => f.imported !== null);
  }

  protected reasonText(reason: string | null): string | null {
    if (!reason || !KNOWN_REASONS.includes(reason)) return null;
    return this.localization.submissions.reason[
      reason as keyof typeof this.localization.submissions.reason
    ];
  }

  protected availableText(deadline: string): string {
    return this.i18n.translate('submissions.drawer.availableText', { hours: hoursLeft(deadline) });
  }

  protected size(bytes: number | null): string {
    return displayBytes(
      bytes,
      this.i18n.currentLanguage(),
      this.localization.submissions.reconcile.none,
    );
  }

  protected preview(file: SubmissionEvidence): void {
    this.previewFailed.set(false);
    this.api
      .evidence(file.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => openBlob(blob),
        error: () => this.previewFailed.set(true),
      });
  }
}
