import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { ImportBatchResult, ImportBatchSummary } from '../../core/import-history.models';
import { ImportHistoryService } from '../../core/import-history.service';
import { currentPeriod } from '../../core/period-options';
import { ReconciliationService } from '../../core/reconciliation.service';
import { HeaderComponent, LayoutIconComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe, AppNumberPipe } from '../../shared/pipes';
import { fill } from '../home/qa-dashboard/qa-dashboard-display';
import { saveFile } from '../performance-dashboard/performance-page';

export type DataImportStep = 'CHOOSE' | 'UPLOAD' | 'RESULT';

/** Reason of a code that still waits for its Core Banking row: the import is what clears it. */
export const WAITING_FOR_FILE_REASON = 'NO_IMPORTED_TRANSACTION';

/** Only the first rejected rows are listed on the result step; the full list stays in the import history. */
export const SHOWN_ERROR_ROWS = 20;

const TEMPLATE_FILE_NAME = 'transaction-import-template.xlsx';

/**
 * SCR-ADM-06 · Data import (UC29, UC31, UC33). Uploads one Core Banking transaction file. The server saves the valid
 * rows and reconciles the pending work transaction codes in the same request, so step 3 shows both results and links
 * to the reconciliation list (SCR-REV-03) already filtered. There is no server-side preview: rows that fail are
 * skipped and reported, and re-importing the same file is safe because duplicates are skipped.
 */
@Component({
  selector: 'app-data-import',
  imports: [
    RouterLink,
    AppDatePipe,
    AppNumberPipe,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
  ],
  templateUrl: './data-import.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './data-import.component.scss',
  ],
})
export class DataImportComponent extends LocalizedComponent {
  private readonly imports = inject(ImportHistoryService);
  private readonly reconciliations = inject(ReconciliationService);
  private readonly evaluations = inject(EvaluationService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly dateTime = 'DD/MM/YYYY HH:mm';
  protected readonly waitingReason = WAITING_FOR_FILE_REASON;
  protected readonly shownErrorRows = SHOWN_ERROR_ROWS;

  protected readonly step = signal<DataImportStep>('CHOOSE');
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly dragging = signal(false);
  protected readonly progress = signal(0);
  protected readonly uploadError = signal<string | null>(null);
  protected readonly uploadErrorDetail = signal<string | null>(null);
  protected readonly result = signal<ImportBatchResult | null>(null);
  protected readonly templateBusy = signal(false);
  protected readonly templateFailed = signal(false);

  /** Context only: the current period and how many of its codes still wait for a file. `null` when unavailable. */
  protected readonly period = signal<EvaluationPeriodOption | null>(null);
  protected readonly waitingCount = signal<number | null>(null);
  protected readonly lastImport = signal<ImportBatchSummary | null>(null);

  protected readonly waitingText = computed(() => {
    const count = this.waitingCount();
    if (count == null) return '';
    const text = this.localization.dataImport;
    const period = this.period();
    return period
      ? fill(text.waitingInPeriod, { period: period.name, count })
      : fill(text.waitingAll, { count });
  });

  protected readonly resultBar = computed(() => {
    const data = this.result();
    if (!data || data.totalRows <= 0) return null;
    const share = (value: number) => (100 * value) / data.totalRows;
    return {
      imported: share(data.importedRows),
      duplicate: share(data.duplicateRows),
      error: share(data.errorRows),
    };
  });

  protected readonly shownErrors = computed(
    () => this.result()?.errors?.slice(0, SHOWN_ERROR_ROWS) ?? [],
  );

  constructor() {
    super();
    this.loadContext();
  }

  protected onFileChosen(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.choose(input.files?.[0] ?? null);
    input.value = '';
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.choose(event.dataTransfer?.files?.[0] ?? null);
  }

  protected removeFile(): void {
    this.file.set(null);
    this.fileError.set(null);
  }

  protected downloadTemplate(): void {
    this.templateBusy.set(true);
    this.templateFailed.set(false);
    this.imports
      .downloadTemplate()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          saveFile(blob, TEMPLATE_FILE_NAME);
          this.templateBusy.set(false);
        },
        error: () => {
          this.templateBusy.set(false);
          this.templateFailed.set(true);
        },
      });
  }

  protected upload(): void {
    const file = this.file();
    if (!file || this.step() === 'UPLOAD') return;
    this.step.set('UPLOAD');
    this.progress.set(0);
    this.uploadError.set(null);
    this.uploadErrorDetail.set(null);
    this.imports
      .upload(file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (event) => {
          if (event.kind === 'progress') {
            this.progress.set(event.percent);
            return;
          }
          this.result.set(event.result);
          this.step.set('RESULT');
          this.loadContext();
        },
        error: (error: unknown) => {
          this.step.set('CHOOSE');
          this.showUploadError(error);
        },
      });
  }

  /** Back to step 1 for another file. */
  protected restart(): void {
    this.file.set(null);
    this.fileError.set(null);
    this.result.set(null);
    this.uploadError.set(null);
    this.uploadErrorDetail.set(null);
    this.step.set('CHOOSE');
  }

  private choose(file: File | null): void {
    this.uploadError.set(null);
    this.uploadErrorDetail.set(null);
    if (!file) return;
    const text = this.localization.dataImport;
    if (!file.name.toLowerCase().endsWith('.xlsx')) {
      this.file.set(null);
      this.fileError.set(text.fileNotXlsx);
      return;
    }
    if (file.size === 0) {
      this.file.set(null);
      this.fileError.set(text.fileEmpty);
      return;
    }
    this.fileError.set(null);
    this.file.set(file);
  }

  private showUploadError(error: unknown): void {
    const text = this.localization.dataImport;
    if (!(error instanceof HttpErrorResponse)) {
      this.uploadError.set(text.uploadFailed);
      return;
    }
    const messages: Record<number, string> = {
      400: text.uploadRejected,
      403: text.uploadForbidden,
      413: text.uploadTooLarge,
    };
    this.uploadError.set(messages[error.status] ?? text.uploadFailed);
    // The server explains a rejected file (which header is missing, empty sheet...) in its own message.
    const detail =
      error.status === 400 ? (error.error as { message?: unknown } | null)?.message : null;
    this.uploadErrorDetail.set(typeof detail === 'string' && detail.trim() ? detail : null);
  }

  /** Period, waiting codes and the last import are supplementary: each one hides itself when its call fails. */
  private loadContext(): void {
    this.evaluations
      .periods()
      .pipe(
        catchError(() => of([] as EvaluationPeriodOption[])),
        switchMap((periods) => {
          const period = currentPeriod(periods);
          this.period.set(period);
          return this.reconciliations
            .list({
              status: 'IN_REVIEW',
              reason: WAITING_FOR_FILE_REASON,
              periodId: period?.id,
              pageSize: 1,
            })
            .pipe(catchError(() => of(null)));
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((list) => this.waitingCount.set(list ? list.totalCount : null));

    this.imports
      .list(1, 1)
      .pipe(
        catchError(() => of(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((page) => this.lastImport.set(page?.items[0] ?? null));
  }
}
