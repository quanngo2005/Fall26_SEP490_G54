import {
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { of, switchMap } from 'rxjs';
import { EvaluationPeriodOption } from '../../../core/evaluation.models';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { KpiDefinition } from '../../../core/kpi-definition.models';
import {
  QuarterSplit,
  QuarterSplitRow,
  QuarterValues,
} from '../../../core/target-assignment.models';
import { TargetAssignmentService } from '../../../core/target-assignment.service';
import { AppNumberPipe } from '../../../shared/pipes';
import {
  DEFAULT_COUNT_STEP,
  DEFAULT_MONEY_STEP,
} from '../../../shared/target/target-shared.models';
import {
  SuggestionBasisDto,
  TargetSetupApiService,
} from '../../target-setup/target-setup-api.service';
import { columnFormat, findQuarterPeriod } from '../target-overview.utils';

type QuarterKey = keyof QuarterValues;
const QUARTERS: readonly QuarterKey[] = ['q1', 'q2', 'q3', 'q4'];

export type QuarterTiming = 'ENDED' | 'IN_PROGRESS' | 'UPCOMING';
export type SplitState = 'MATCHED' | 'MISMATCH' | 'NOT_SPLIT' | 'FIXED';

interface QuarterView {
  number: number;
  target: number | null;
  share: number | null;
  previous: number | null;
  change: number | null;
  timing: QuarterTiming | null;
}

/** Percent change from `previous` to `current`, or null when there is nothing to compare. */
function changeOf(current: number | null, previous: number | null): number | null {
  return current === null || !previous ? null : ((current - previous) / previous) * 100;
}

/** SCR-STRAT-03.1-M1: one KPI's year target split by quarter, opened from the branch overview. */
@Component({
  selector: 'app-kpi-quarter-modal',
  imports: [AppNumberPipe],
  templateUrl: './kpi-quarter-modal.component.html',
  styleUrls: [
    '../../../shared/ui/ui-controls.scss',
    '../../../shared/ui/workspace-page.part2.scss',
    '../../../shared/ui/modal.scss',
    './kpi-quarter-modal.component.scss',
  ],
})
export class KpiQuarterModalComponent extends LocalizedComponent implements OnInit {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly suggestionApi = inject(TargetSetupApiService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly branchId = input.required<string>();
  readonly branchName = input('');
  readonly year = input.required<number>();
  readonly kpi = input.required<KpiDefinition>();
  readonly periods = input<readonly EvaluationPeriodOption[]>([]);
  readonly canManage = input(false);
  readonly closed = output<void>();

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly saving = signal(false);
  protected readonly saveFailed = signal(false);
  protected readonly suggesting = signal(false);
  protected readonly split = signal<QuarterSplit | null>(null);

  protected readonly row = computed<QuarterSplitRow | null>(
    () => this.split()?.rows.find((r) => r.kpiId === this.kpi().id) ?? null,
  );

  protected readonly state = computed<SplitState>(() => {
    const row = this.row();
    if (!row || row.isRatio) return 'FIXED';
    if (QUARTERS.every((key) => row[key] === null)) return 'NOT_SPLIT';
    return row.matched ? 'MATCHED' : 'MISMATCH';
  });

  /** Display unit and divisor for every value in the popup. */
  protected readonly format = computed(() => {
    const row = this.row();
    const values = row
      ? [row.yearValue, ...QUARTERS.map((key) => row[key] ?? 0), row.previousYearValue ?? 0]
      : [];
    return columnFormat(this.kpi().unit, values, {
      billionVnd: this.localization.targetOverview.unitBillionVnd,
      millionVnd: this.localization.targetOverview.unitMillionVnd,
    });
  });

  protected readonly quarters = computed<QuarterView[]>(() => {
    const row = this.row();
    if (!row) return [];
    const today = new Date().toISOString().slice(0, 10);
    return QUARTERS.map((key, index) => {
      const target = row[key];
      const previous = row.previousYear?.[key] ?? null;
      const period = findQuarterPeriod(this.periods(), this.year(), index + 1);
      const timing: QuarterTiming | null = !period
        ? null
        : period.endDate < today
          ? 'ENDED'
          : period.startDate <= today
            ? 'IN_PROGRESS'
            : 'UPCOMING';
      return {
        number: index + 1,
        target,
        share:
          target !== null && row.yearValue && !row.isRatio ? (target / row.yearValue) * 100 : null,
        previous,
        change: changeOf(target, previous),
        timing,
      };
    });
  });

  protected readonly yearChange = computed(() => {
    const row = this.row();
    return row ? changeOf(row.yearValue, row.previousYearValue) : null;
  });

  /** Quarter changes go through the draft quarter split; a published year or a fixed KPI is read only. */
  protected readonly editable = computed(
    () =>
      this.canManage() && this.split()?.status === 'DRAFT' && !!this.row() && !this.row()!.isRatio,
  );

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.targetApi
      .getSetup(this.branchId(), this.year())
      .pipe(
        switchMap((setup) =>
          setup.branchYearId ? this.targetApi.getQuarterSplit(setup.branchYearId) : of(null),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (split) => {
          this.split.set(split);
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected shown(value: number | null): number | null {
    return value === null ? null : Math.round((value / this.format().divisor) * 100) / 100;
  }

  /** A percentage rounded to `digits` decimals, without trailing zeros once formatted. */
  protected pct(value: number, digits = 1): number {
    const factor = 10 ** digits;
    return Math.round(value * factor) / factor;
  }

  /**
   * "Dùng gợi ý": asks `POST /target-suggestions/{year}` (tier 1) for this KPI by `basis` and saves the result.
   * Locked quarters keep their values; without history the server splits progressively 1:2:3:4.
   */
  protected applySuggestion(basis: SuggestionBasisDto): void {
    const split = this.split();
    const row = this.row();
    if (!split || !row || !this.editable() || this.saving() || this.suggesting()) return;
    const locked = this.lockedFlags();
    this.suggesting.set(true);
    this.saveFailed.set(false);
    this.suggestionApi
      .suggest(this.year(), {
        tier: 1,
        basis,
        anchorId: split.branchYearId,
        kpiIds: [row.kpiId],
        departmentId: null,
        employees: null,
        periods: 'YEAR_PLUS_4Q',
        overwrite: 'UNLOCKED',
        currentValues: [
          {
            unitId: row.kpiId,
            year: row.yearValue,
            q1: locked[0] ? row.q1 : null,
            q2: locked[1] ? row.q2 : null,
            q3: locked[2] ? row.q3 : null,
            q4: locked[3] ? row.q4 : null,
            locked: false,
          },
        ],
        rounding: { moneyStep: DEFAULT_MONEY_STEP, countStep: DEFAULT_COUNT_STEP, mode: 'NEAREST' },
        remainder: { method: 'LARGEST_REMAINDER', bufferUnitId: null },
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          const cell = response.kpis[0]?.cells[0];
          this.suggesting.set(false);
          if (
            !cell ||
            cell.q1 === null ||
            cell.q2 === null ||
            cell.q3 === null ||
            cell.q4 === null
          ) {
            this.saveFailed.set(true);
            return;
          }
          this.save([cell.q1, cell.q2, cell.q3, cell.q4]);
        },
        error: () => {
          this.suggesting.set(false);
          this.saveFailed.set(true);
        },
      });
  }

  protected openSetup(): void {
    this.closed.emit();
    void this.router.navigate(['/target-setup', this.year()]);
  }

  protected close(): void {
    this.closed.emit();
  }

  private lockedFlags(): boolean[] {
    const quarters = this.split()?.quarters ?? [];
    return [1, 2, 3, 4].map((n) => quarters.find((q) => q.quarter === n)?.locked ?? false);
  }

  private save(values: number[]): void {
    const split = this.split();
    if (!split || !this.editable() || this.saving()) return;
    this.saving.set(true);
    this.saveFailed.set(false);
    const [q1, q2, q3, q4] = values;
    this.targetApi
      .saveQuarterSplit(split.branchYearId, { rows: [{ kpiId: this.kpi().id, q1, q2, q3, q4 }] })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (next) => {
          this.split.set(next);
          this.saving.set(false);
        },
        error: () => {
          this.saveFailed.set(true);
          this.saving.set(false);
        },
      });
  }
}
