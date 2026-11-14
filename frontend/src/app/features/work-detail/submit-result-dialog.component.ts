import { A11yModule } from '@angular/cdk/a11y';
import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  HostListener,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, map, of, switchMap } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  SubmissionContext,
  SubmissionKpiCriteria,
  SubmissionMetricInput,
} from '../../core/result-submission.models';
import { ResultSubmissionService } from '../../core/result-submission.service';
import { WorkDetail } from '../../core/work.models';
import { dateOnly } from '../work-board/work-board.utils';

/** Same limits as the server. */
export const MAX_TRANSACTION_CODES = 10;
const MAX_CODE_LENGTH = 100;
const MAX_RESULT_LENGTH = 2000;
const DEFAULT_MAX_FILE_BYTES = 26_214_400;
const BYTES_PER_MB = 1_048_576;

type LoadState = 'loading' | 'ready' | 'error';

/** One KPI line of the dialog; the values stay text until they are validated. */
interface MetricDraft {
  criteria: SubmissionKpiCriteria;
  value: string;
  denominator: string;
}

export interface ResultSubmitted {
  submissionId: string;
  /** The result is saved, but the transaction codes could not be attached. */
  codesFailed: boolean;
}

/** Splits what the employee typed into distinct transaction codes (comma, semicolon, space or line break). */
export function parseTransactionCodes(raw: string): string[] {
  const seen = new Set<string>();
  for (const part of raw.split(/[\s,;]+/)) {
    const code = part.trim();
    if (code) seen.add(code);
  }
  return [...seen];
}

export function fileExtension(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot < 0 ? '' : name.slice(dot).toLowerCase();
}

/**
 * SCR-REV-02-M1: the assignee submits the result of a work (`POST /works/{id}/submissions`) with the result text, the
 * evidence and, when the work type requires it, the KPI values. Transaction codes go to `POST /works/{id}/transactions`
 * right after, so the server can reconcile them against the imported data.
 */
@Component({
  selector: 'app-submit-result-dialog',
  standalone: true,
  imports: [A11yModule, FormsModule],
  templateUrl: './submit-result-dialog.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/dialog.scss',
    './submit-result-dialog.component.scss',
  ],
})
export class SubmitResultDialogComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(ResultSubmissionService);
  private readonly destroyRef = inject(DestroyRef);

  readonly work = input.required<WorkDetail>();
  readonly closed = output<void>();
  readonly submittedResult = output<ResultSubmitted>();

  protected readonly maxCodes = MAX_TRANSACTION_CODES;
  protected readonly maxResult = MAX_RESULT_LENGTH;

  protected readonly state = signal<LoadState>('loading');
  protected readonly context = signal<SubmissionContext | null>(null);
  protected readonly metrics = signal<MetricDraft[]>([]);
  protected readonly files = signal<File[]>([]);
  protected readonly fileErrors = signal<string[]>([]);
  protected readonly dragging = signal(false);
  protected readonly attempted = signal(false);
  protected readonly saving = signal(false);
  protected readonly errorText = signal<string | null>(null);

  protected codesText = '';
  protected result = '';

  protected readonly allowedTypes = computed(() => this.context()?.allowedFileTypes ?? []);
  protected readonly maxFiles = computed(() => this.context()?.maxFileCount ?? 10);
  protected readonly maxFileMb = computed(() =>
    Math.round((this.context()?.maxFileSizeBytes ?? DEFAULT_MAX_FILE_BYTES) / BYTES_PER_MB),
  );
  protected readonly typeList = computed(() =>
    this.allowedTypes()
      .map((type) => type.replace('.', '').toUpperCase())
      .join(', '),
  );
  protected readonly acceptAttr = computed(() => this.allowedTypes().join(','));

  protected get codes(): string[] {
    return parseTransactionCodes(this.codesText);
  }

  protected get resultMissing(): boolean {
    return !this.result.trim();
  }

  protected get codesTooMany(): boolean {
    return this.codes.length > MAX_TRANSACTION_CODES;
  }

  protected get codeTooLong(): boolean {
    return this.codes.some((code) => code.length > MAX_CODE_LENGTH);
  }

  /** A work with neither a transaction code nor evidence leaves the manager nothing to check. */
  protected get nothingToCheck(): boolean {
    return this.codes.length === 0 && this.files().length === 0;
  }

  protected get invalid(): boolean {
    return (
      this.resultMissing ||
      this.codesTooMany ||
      this.codeTooLong ||
      this.nothingToCheck ||
      this.metrics().some((m) => this.metricValueError(m) || this.metricDenominatorError(m))
    );
  }

  ngOnInit(): void {
    this.api
      .getSubmissionContext(this.work().id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (context) => {
          this.context.set(context);
          this.metrics.set(
            context.kpiCriteria
              .filter((criteria) => criteria.isRequired)
              .map((criteria) => ({ criteria, value: '', denominator: '' })),
          );
          this.state.set('ready');
        },
        error: () => this.state.set('error'),
      });
  }

  @HostListener('document:keydown.escape')
  protected close(): void {
    if (!this.saving()) this.closed.emit();
  }

  protected deadlineText(): string | null {
    const end = this.work().plannedEndDate;
    if (!end) return null;
    const date = this.i18n.formatDate(dateOnly(end));
    const key = this.isLate() ? 'work.detail.submit.deadlineLate' : 'work.detail.submit.deadline';
    return this.i18n.translate(key, { date });
  }

  protected isLate(): boolean {
    const end = this.work().plannedEndDate;
    return !!end && dateOnly(end) < new Date(new Date().setHours(0, 0, 0, 0));
  }

  protected title(): string {
    return this.i18n.translate('work.detail.submit.title', { code: this.work().code });
  }

  protected subtitle(): string {
    const context = this.context();
    return this.i18n.translate('work.detail.submit.subtitle', {
      type: context?.workTypeName ?? this.localization.work.detail.submit.noWorkType,
      no: context?.nextSubmissionNo ?? 1,
    });
  }

  protected isRatio(draft: MetricDraft): boolean {
    return draft.criteria.aggregationType.toUpperCase() === 'RATIO';
  }

  protected metricValueError(draft: MetricDraft): boolean {
    const value = Number(draft.value);
    return draft.value.trim() === '' || !Number.isFinite(value) || value < 0;
  }

  protected metricDenominatorError(draft: MetricDraft): boolean {
    if (!this.isRatio(draft)) return false;
    const value = Number(draft.denominator);
    return draft.denominator.trim() === '' || !Number.isFinite(value) || value <= 0;
  }

  /** A number input reports a number (or null when it is empty or half-typed), never a string. */
  protected setMetric(
    index: number,
    field: 'value' | 'denominator',
    entered: string | number | null,
  ): void {
    const text = entered === null || entered === undefined ? '' : String(entered);
    this.metrics.update((list) =>
      list.map((draft, i) => (i === index ? { ...draft, [field]: text } : draft)),
    );
  }

  // ---- files ----------------------------------------------------------------------------------------

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.addFiles(Array.from(input.files ?? []));
    input.value = '';
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDragLeave(): void {
    this.dragging.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.addFiles(Array.from(event.dataTransfer?.files ?? []));
  }

  protected removeFile(file: File): void {
    this.files.update((list) => list.filter((f) => f !== file));
    this.fileErrors.set([]);
  }

  protected sizeText(file: File): string {
    return file.size >= BYTES_PER_MB
      ? `${(file.size / BYTES_PER_MB).toFixed(1)} MB`
      : `${Math.max(1, Math.round(file.size / 1024))} KB`;
  }

  private addFiles(incoming: File[]): void {
    const limit = this.context()?.maxFileSizeBytes ?? DEFAULT_MAX_FILE_BYTES;
    const allowed = this.allowedTypes();
    const accepted = [...this.files()];
    const errors: string[] = [];
    for (const file of incoming) {
      if (allowed.length > 0 && !allowed.includes(fileExtension(file.name))) {
        errors.push(this.i18n.translate('work.detail.submit.errors.fileType', { name: file.name }));
      } else if (file.size === 0) {
        errors.push(
          this.i18n.translate('work.detail.submit.errors.fileEmpty', { name: file.name }),
        );
      } else if (file.size > limit) {
        errors.push(
          this.i18n.translate('work.detail.submit.errors.fileSize', {
            name: file.name,
            max: this.maxFileMb(),
          }),
        );
      } else if (accepted.length >= this.maxFiles()) {
        errors.push(
          this.i18n.translate('work.detail.submit.errors.fileCount', { max: this.maxFiles() }),
        );
        break;
      } else if (!accepted.some((f) => f.name === file.name && f.size === file.size)) {
        accepted.push(file);
      }
    }
    this.files.set(accepted);
    this.fileErrors.set(errors);
  }

  // ---- submit ---------------------------------------------------------------------------------------

  protected submit(): void {
    this.attempted.set(true);
    if (this.saving() || this.state() !== 'ready' || this.invalid) return;

    const form = new FormData();
    form.append('comment', this.result.trim());
    const metricItems: SubmissionMetricInput[] = this.metrics().map((m) => ({
      kpiDefinitionId: m.criteria.kpiDefinitionId,
      value: Number(m.value),
      ...(this.isRatio(m) ? { denominator: Number(m.denominator) } : {}),
    }));
    if (metricItems.length > 0) form.append('metricItemsJson', JSON.stringify(metricItems));
    for (const file of this.files()) form.append('files', file, file.name);

    const codes = this.codes;
    const workId = this.work().id;
    this.saving.set(true);
    this.errorText.set(null);
    this.api
      .createSubmission(workId, form)
      .pipe(
        // The result is already saved here: a code that cannot be attached is reported, never undone.
        switchMap((created) =>
          codes.length === 0
            ? of<ResultSubmitted>({ submissionId: created.submissionId, codesFailed: false })
            : this.api.addTransactionCodes(workId, codes).pipe(
                map((): ResultSubmitted => ({
                  submissionId: created.submissionId,
                  codesFailed: false,
                })),
                catchError(() =>
                  of<ResultSubmitted>({ submissionId: created.submissionId, codesFailed: true }),
                ),
              ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (outcome) => {
          this.saving.set(false);
          this.submittedResult.emit(outcome);
        },
        error: (error: HttpErrorResponse) => {
          this.saving.set(false);
          this.errorText.set(this.messageFor(error));
        },
      });
  }

  private messageFor(error: HttpErrorResponse): string {
    const text = this.localization.work.detail.submit.errors;
    if (error.status === 403) return text.forbidden;
    if (error.status === 409) return text.conflict;
    if (error.status === 400 || error.status === 422) {
      const details: unknown = error.error?.errors;
      if (Array.isArray(details) && typeof details[0] === 'string' && details[0].trim()) {
        // The server sends "Field: message"; the employee only needs the message.
        return details[0].replace(/^\w+:\s+/, '');
      }
      const message: unknown = error.error?.message;
      return typeof message === 'string' && message.trim() ? message : text.invalid;
    }
    return text.generic;
  }
}
