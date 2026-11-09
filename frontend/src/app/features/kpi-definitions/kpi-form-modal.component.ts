import { HttpErrorResponse } from '@angular/common/http';
import { A11yModule } from '@angular/cdk/a11y';
import {
  Component,
  DestroyRef,
  HostListener,
  OnInit,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import {
  Observable,
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  switchMap,
} from 'rxjs';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { JobPositionSummary } from '../../core/job-position.models';
import {
  KPI_AGGREGATIONS,
  KPI_CREDIT_ROLES,
  KPI_DATA_SOURCES,
  KPI_DIRECTIONS,
  KpiAggregationType,
  KpiCreditRole,
  KpiDataSource,
  KpiDefinition,
  KpiDefinitionSavePayload,
  KpiDirection,
  KpiGroupOption,
} from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { AGGREGATION_KEYS, DIRECTION_KEYS, periodShort } from './kpi-definitions.utils';

export type KpiFormMode = 'create' | 'edit' | 'request';

/** The form fields; numbers stay text while typing and are parsed on save. */
export interface KpiFormValues {
  kpiGroupId: string;
  name: string;
  positionIds: string[];
  creditRole: KpiCreditRole;
  dataSource: KpiDataSource;
  productName: string;
  unit: string;
  direction: KpiDirection;
  aggregationType: KpiAggregationType;
  fromPeriodId: string;
  toPeriodId: string;
  maxAchievementPercent: string;
  baselineValue: string;
  formula: string;
  description: string;
  requestNote: string;
}

export interface KpiFormResult {
  kpi: KpiDefinition;
  /** The KPI was also sent for approval. */
  submitted: boolean;
  /** Saving worked but sending for approval failed; the KPI stays a draft. */
  submitFailed: boolean;
  /** The user asked to create the Processor item of the same product next. */
  pair: boolean;
}

const MAX_NOTE_LENGTH = 500;
const CODE_DEBOUNCE_MS = 300;

/** SCR-STRAT-02-M1: create, edit or request a KPI. */
@Component({
  selector: 'app-kpi-form-modal',
  standalone: true,
  imports: [A11yModule, FormsModule],
  templateUrl: './kpi-form-modal.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/dialog.scss',
    './kpi-dialog.scss',
  ],
})
export class KpiFormModalComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(KpiDefinitionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly typedName = new Subject<string>();

  readonly mode = input.required<KpiFormMode>();
  /** The KPI being edited. */
  readonly kpi = input<KpiDefinition | null>(null);
  readonly groups = input.required<readonly KpiGroupOption[]>();
  readonly positions = input.required<readonly JobPositionSummary[]>();
  /** Quarter periods a KPI can start or stop in. */
  readonly periods = input.required<readonly EvaluationPeriodOption[]>();
  readonly products = input<readonly string[]>([]);
  readonly units = input<readonly string[]>([]);
  /** Starting values, for the Processor item that goes with a Referrer item. */
  readonly prefill = input<Partial<KpiFormValues> | null>(null);
  /** Whether a draft or rejected KPI can also be sent for approval from here. */
  readonly canSubmit = input(true);

  readonly closed = output<void>();
  readonly saved = output<KpiFormResult>();

  protected readonly directions = KPI_DIRECTIONS;
  protected readonly aggregations = KPI_AGGREGATIONS;
  protected readonly creditRoles = KPI_CREDIT_ROLES;
  protected readonly dataSources = KPI_DATA_SOURCES;
  protected readonly maxNote = MAX_NOTE_LENGTH;
  protected readonly directionKey = DIRECTION_KEYS;
  protected readonly aggregationKey = AGGREGATION_KEYS;

  protected values: KpiFormValues = this.blank();
  /** Made from the name by the server; a signal because it arrives after the form was drawn. */
  protected readonly code = signal('');
  protected pair = false;

  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);
  protected readonly errors = signal<
    Partial<Record<keyof KpiFormValues | 'code' | 'form', string>>
  >({});

  ngOnInit(): void {
    const existing = this.kpi();
    this.values = {
      ...(existing ? this.valuesOf(existing) : this.blank()),
      ...(this.prefill() ?? {}),
    };
    this.code.set(existing?.code ?? '');
    // The code is made from the name: ask the server for it once the user stops typing.
    if (this.mode() !== 'edit') {
      this.typedName
        .pipe(
          map((name) => name.trim()),
          distinctUntilChanged(),
          debounceTime(CODE_DEBOUNCE_MS),
          switchMap((name) =>
            name ? this.api.codeSuggestion(name).pipe(catchError(() => of(''))) : of(''),
          ),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe((code) => this.code.set(code));
    }
  }

  protected onNameChange(name: string): void {
    this.typedName.next(name);
  }

  /** The Referrer item usually comes with a Processor item of the same product. */
  protected get offersPair(): boolean {
    return this.mode() === 'create' && this.values.creditRole === 'REFERRER';
  }

  /** Saving a KPI that is still a draft or was rejected can send it on at once. */
  protected get canSendOnSave(): boolean {
    if (this.mode() === 'request' || !this.canSubmit()) return false;
    const status = this.kpi()?.status;
    return status === undefined || status === 'DRAFT' || status === 'REJECTED';
  }

  protected periodLabel(period: EvaluationPeriodOption): string {
    return periodShort({ code: period.code });
  }

  protected toggleSelect(list: string[], id: string): string[] {
    return list.includes(id) ? list.filter((item) => item !== id) : [...list, id];
  }

  @HostListener('document:keydown.escape')
  protected close(): void {
    if (!this.saving()) this.closed.emit();
  }

  @HostListener('document:keydown.control.s', ['$event'])
  protected onSaveShortcut(event: Event): void {
    event.preventDefault();
    this.submit(this.canSendOnSave || this.mode() === 'request');
  }

  /** `sendForApproval` also moves a draft to "awaiting approval"; for a request it is ignored. */
  protected submit(sendForApproval: boolean): void {
    this.submitted.set(true);
    const errors = this.validate();
    this.errors.set(errors);
    if (Object.keys(errors).length > 0 || this.saving()) return;

    this.saving.set(true);
    const payload = this.payload();
    const mode = this.mode();
    const send = sendForApproval && this.canSendOnSave;

    const save$: Observable<KpiFormResult> =
      mode === 'request'
        ? this.api
            .request({ kpi: payload, requestNote: this.values.requestNote.trim() })
            .pipe(map((kpi) => ({ kpi, submitted: false, submitFailed: false, pair: false })))
        : (mode === 'edit'
            ? this.api.update(this.kpi()!.id, payload)
            : this.api.create(payload)
          ).pipe(
            switchMap((kpi) =>
              send
                ? this.api.changeStatus(kpi.id, 'PENDING_APPROVAL').pipe(
                    map(() => ({ kpi, submitted: true, submitFailed: false, pair: this.pair })),
                    catchError(() =>
                      of({ kpi, submitted: false, submitFailed: true, pair: this.pair }),
                    ),
                  )
                : of({ kpi, submitted: false, submitFailed: false, pair: this.pair }),
            ),
          );

    save$.subscribe({
      next: (result) => {
        this.saving.set(false);
        this.saved.emit(result);
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.errors.set(this.serverErrors(error));
      },
    });
  }

  private validate(): Partial<Record<keyof KpiFormValues | 'code' | 'form', string>> {
    const text = this.localization.kpiCatalog;
    const v = this.values;
    const errors: Partial<Record<keyof KpiFormValues | 'code' | 'form', string>> = {};
    const required = (key: keyof KpiFormValues, ok: boolean) => {
      if (!ok) errors[key] = text.required;
    };

    required('kpiGroupId', v.kpiGroupId !== '');
    required('name', v.name.trim().length > 0);
    required('positionIds', v.positionIds.length > 0);
    required('unit', v.unit.trim().length > 0);
    required('fromPeriodId', v.fromPeriodId !== '');
    if (this.mode() === 'request') required('requestNote', v.requestNote.trim().length > 0);

    if (this.isBadNumber(v.maxAchievementPercent, false))
      errors.maxAchievementPercent = text.invalidNumber;
    if (this.isBadNumber(v.baselineValue, true)) errors.baselineValue = text.invalidNumber;

    const from = this.period(v.fromPeriodId);
    const to = this.period(v.toPeriodId);
    if (from && to && to.startDate < from.startDate) errors.toPeriodId = text.periodOrder;
    return errors;
  }

  /** Empty is fine; otherwise a number above zero (the cap) or zero and up (the baseline). */
  private isBadNumber(value: string, allowZero: boolean): boolean {
    if (value.trim() === '') return false;
    const number = Number(value);
    return !Number.isFinite(number) || (allowZero ? number < 0 : number <= 0);
  }

  private period(id: string): EvaluationPeriodOption | undefined {
    return this.periods().find((period) => period.id === id);
  }

  private serverErrors(
    error: HttpErrorResponse,
  ): Partial<Record<keyof KpiFormValues | 'code' | 'form', string>> {
    const text = this.localization.kpiCatalog;
    const message: string = typeof error.error?.message === 'string' ? error.error.message : '';
    if (error.status === 409) return { code: text.toastDuplicate };
    if (message.includes('MSG27')) return { toPeriodId: text.periodOrder };
    return { form: error.status === 400 && message ? message : text.toastFailed };
  }

  private payload(): KpiDefinitionSavePayload {
    const v = this.values;
    const number = (value: string) => (value.trim() === '' ? null : Number(value));
    return {
      kpiGroupId: v.kpiGroupId,
      code: this.code(),
      name: v.name.trim(),
      description: v.description.trim() || null,
      formula: v.formula.trim() || null,
      unit: v.unit.trim(),
      aggregationType: v.aggregationType,
      direction: v.direction,
      maxAchievementPercent: number(v.maxAchievementPercent),
      baselineValue: number(v.baselineValue),
      creditRole: v.creditRole,
      dataSource: v.dataSource,
      productName: v.productName.trim() || null,
      jobPositionIds: v.positionIds,
      effectiveFromPeriodId: v.fromPeriodId || null,
      effectiveToPeriodId: v.toPeriodId || null,
    };
  }

  private valuesOf(kpi: KpiDefinition): KpiFormValues {
    return {
      kpiGroupId: kpi.kpiGroupId,
      name: kpi.name,
      positionIds: kpi.jobPositions.map((position) => position.id),
      creditRole: kpi.creditRole,
      dataSource: kpi.dataSource,
      productName: kpi.productName ?? '',
      unit: kpi.unit,
      direction: kpi.direction,
      aggregationType: kpi.aggregationType,
      fromPeriodId: kpi.effectiveFrom?.id ?? '',
      toPeriodId: kpi.effectiveTo?.id ?? '',
      maxAchievementPercent:
        kpi.maxAchievementPercent === null ? '' : String(kpi.maxAchievementPercent),
      baselineValue: kpi.baselineValue === null ? '' : String(kpi.baselineValue),
      formula: kpi.formula ?? '',
      description: kpi.description ?? '',
      requestNote: '',
    };
  }

  private blank(): KpiFormValues {
    return {
      kpiGroupId: '',
      name: '',
      positionIds: [],
      creditRole: 'PROCESSOR',
      dataSource: 'TRANSACTION',
      productName: '',
      unit: '',
      direction: 'HIGHER_BETTER',
      aggregationType: 'SUM',
      fromPeriodId: '',
      toPeriodId: '',
      maxAchievementPercent: '',
      baselineValue: '',
      formula: '',
      description: '',
      requestNote: '',
    };
  }
}
