import { Component, computed, HostListener, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { SystemParameterService } from '../../core/system-parameter.service';
import {
  EvaluationComparisonBaseline,
  EvaluationComparisonSetting,
  SystemParameter,
  SystemParameterChange,
  SystemParameterGroupCode,
  SystemParameterKey,
  SystemParameterValueType,
} from '../../core/system-parameter.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { ConfirmSaveDialogComponent } from '../../shared/ui/confirm-save-dialog.component';

type UnitId =
  | 'hours'
  | 'minutes'
  | 'days'
  | 'hourOfDay'
  | 'megabytes'
  | 'rows'
  | 'codes'
  | 'files'
  | 'chars'
  | 'percent'
  | 'points'
  | 'multiplier'
  | 'employees'
  | 'periods'
  | 'attempts'
  | 'perMinute'
  | 'time'
  | 'vnd';

type ValidationErrorKind =
  'invalidRange' | 'invalidDecimalRange' | 'invalidPositive' | 'invalidTime' | null;

interface ParameterGroup {
  id: SystemParameterGroupCode;
  keys: SystemParameterKey[];
}

interface ParameterRow {
  parameter: SystemParameter;
  valueType: SystemParameterValueType;
  text: string;
  boolValue: boolean;
  unit: UnitId | null;
  changed: boolean;
  error: ValidationErrorKind;
}

export interface ParameterDiff {
  key: SystemParameterKey;
  oldValue: number | string | boolean;
  newValue: number | string | boolean;
}

const GROUPS: readonly ParameterGroup[] = [
  {
    id: 'DEADLINES',
    keys: [
      'kpiLockTime',
      'preCloseCheckDays',
      'preCloseDeadlineDays',
      'preCloseDeadlineHour',
      'manualApprovalHours',
      'workOverdueEnabled',
      'workOverdueSweepMinutes',
      'campaignExclusivityDays',
    ],
  },
  {
    id: 'APPEALS',
    keys: ['appealRejectedHours', 'appealWindowDays', 'minReasonLength', 'maxEvidenceFiles'],
  },
  {
    id: 'SCORING_QA',
    keys: [
      'qaSampleRatePercent',
      'riskPenaltyLow',
      'riskPenaltyMedium',
      'riskPenaltyHigh',
      'riskPenaltyCritical',
      'unitUnderperformanceThresholdPercent',
      'unitPenaltyMultiplier',
    ],
  },
  {
    id: 'QUOTA_RANKING',
    keys: [
      'tierAQuotaPercent',
      'tierBTargetPercent',
      'tierCTargetPercent',
      'tierDTargetPercent',
      'minDeptSizeForOwnQuota',
    ],
  },
  {
    id: 'IMPORT_HANDOVER',
    keys: [
      'importMaxFileMb',
      'targetImportMaxFileMb',
      'importMaxRows',
      'maxTransactionsPerWork',
      'handoverThresholdVnd',
      'importFormats',
    ],
  },
  {
    id: 'DASHBOARD_SECURITY',
    keys: [
      'perfAttentionDropPoints',
      'perfAttentionLowScore',
      'perfAttentionTopN',
      'perfWeakGroupScore',
      'perfTrendPeriodCount',
      'authMaxFailedAttempts',
      'authLockoutMinutes',
      'loginRateLimitPerMinute',
    ],
  },
];

const UNITS: Readonly<Partial<Record<SystemParameterKey, UnitId>>> = {
  kpiLockTime: 'time',
  preCloseCheckDays: 'days',
  preCloseDeadlineDays: 'days',
  preCloseDeadlineHour: 'hourOfDay',
  manualApprovalHours: 'hours',
  workOverdueSweepMinutes: 'minutes',
  campaignExclusivityDays: 'days',
  appealRejectedHours: 'hours',
  appealWindowDays: 'days',
  minReasonLength: 'chars',
  maxEvidenceFiles: 'files',
  qaSampleRatePercent: 'percent',
  riskPenaltyLow: 'points',
  riskPenaltyMedium: 'points',
  riskPenaltyHigh: 'points',
  riskPenaltyCritical: 'points',
  unitUnderperformanceThresholdPercent: 'percent',
  unitPenaltyMultiplier: 'multiplier',
  tierAQuotaPercent: 'percent',
  tierBTargetPercent: 'percent',
  tierCTargetPercent: 'percent',
  tierDTargetPercent: 'percent',
  minDeptSizeForOwnQuota: 'employees',
  importMaxFileMb: 'megabytes',
  targetImportMaxFileMb: 'megabytes',
  importMaxRows: 'rows',
  maxTransactionsPerWork: 'codes',
  handoverThresholdVnd: 'vnd',
  perfAttentionDropPoints: 'points',
  perfAttentionLowScore: 'points',
  perfAttentionTopN: 'employees',
  perfWeakGroupScore: 'points',
  perfTrendPeriodCount: 'periods',
  authMaxFailedAttempts: 'attempts',
  authLockoutMinutes: 'minutes',
  loginRateLimitPerMinute: 'perMinute',
};

const FALLBACK_TYPES: Readonly<Partial<Record<SystemParameterKey, SystemParameterValueType>>> = {
  kpiLockTime: 'TIME',
  workOverdueEnabled: 'BOOLEAN',
  importFormats: 'STRING',
  qaSampleRatePercent: 'PERCENT',
  riskPenaltyLow: 'DECIMAL',
  riskPenaltyMedium: 'DECIMAL',
  riskPenaltyHigh: 'DECIMAL',
  riskPenaltyCritical: 'DECIMAL',
  unitUnderperformanceThresholdPercent: 'PERCENT',
  unitPenaltyMultiplier: 'DECIMAL',
  tierAQuotaPercent: 'PERCENT',
  tierBTargetPercent: 'PERCENT',
  tierCTargetPercent: 'PERCENT',
  tierDTargetPercent: 'PERCENT',
  perfAttentionDropPoints: 'DECIMAL',
  perfAttentionLowScore: 'DECIMAL',
  perfWeakGroupScore: 'DECIMAL',
};

/** SCR-ADM-04 / GB-910: Operating parameters & weights workspace. */
@Component({
  selector: 'app-system-parameters',
  imports: [
    RouterLink,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    ConfirmSaveDialogComponent,
  ],
  templateUrl: './system-parameters.component.html',
  styleUrls: [
    './admin-config-shell.scss',
    './system-parameters.component.scss',
    '../sidebar-settings/diff-table.scss',
  ],
})
export class SystemParametersComponent extends LocalizedComponent {
  private readonly api = inject(SystemParameterService);

  readonly auditEntityName = 'SystemParameter';
  readonly allGroups = GROUPS;
  readonly baselineOptions: readonly EvaluationComparisonBaseline[] = [
    'PREVIOUS_PERIOD',
    'SAME_PERIOD_LAST_YEAR',
    'AVERAGE_LAST_N',
  ];

  readonly parameters = signal<SystemParameter[]>([]);
  readonly drafts = signal<Partial<Record<SystemParameterKey, string>>>({});
  readonly searchQuery = signal('');
  readonly activeGroup = signal<'ALL' | SystemParameterGroupCode>('ALL');

  readonly comparison = signal<EvaluationComparisonSetting>({
    baseline: 'PREVIOUS_PERIOD',
    lookbackPeriods: 3,
  });
  readonly comparisonSaving = signal(false);
  readonly comparisonSaved = signal(false);

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<'loadFailed' | 'saveFailed' | null>(null);
  readonly saved = signal(false);
  readonly confirming = signal(false);

  readonly rows = computed(() => {
    const drafts = this.drafts();
    return new Map<SystemParameterKey, ParameterRow>(
      this.parameters().map((parameter) => {
        const valueType = this.resolveValueType(parameter);
        const draft = drafts[parameter.key];
        const baseText = String(parameter.value);
        const text = draft ?? baseText;
        const changed = draft !== undefined && draft !== baseText;
        return [
          parameter.key,
          {
            parameter,
            valueType,
            text,
            boolValue: text === 'true',
            unit: UNITS[parameter.key] ?? null,
            changed,
            error: changed ? this.validate(parameter, valueType, text) : null,
          },
        ];
      }),
    );
  });

  readonly visibleGroups = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    const selected = this.activeGroup();
    const dict = this.localization.systemParameters.params;
    return GROUPS.filter((g) => selected === 'ALL' || g.id === selected)
      .map((group) => ({
        id: group.id,
        keys: group.keys.filter((key) => {
          if (!this.rows().has(key)) return false;
          if (!query) return true;
          const meta = dict[key];
          return (
            key.toLowerCase().includes(query) ||
            meta.label.toLowerCase().includes(query) ||
            meta.help.toLowerCase().includes(query)
          );
        }),
      }))
      .filter((g) => g.keys.length > 0);
  });

  readonly diffs = computed<ParameterDiff[]>(() =>
    [...this.rows().values()]
      .filter((row) => row.changed && !row.error)
      .map((row) => ({
        key: row.parameter.key,
        oldValue: row.parameter.value,
        newValue: this.parseWireValue(row),
      })),
  );
  readonly changeCount = computed(() => [...this.rows().values()].filter((r) => r.changed).length);
  readonly dirty = computed(() => this.changeCount() > 0);
  readonly canSave = computed(
    () => this.dirty() && this.diffs().length === this.changeCount() && !this.saving(),
  );

  constructor() {
    super();
    this.load();
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
      event.preventDefault();
      this.requestSave();
    }
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    const comparison$ =
      typeof this.api.getComparison === 'function'
        ? this.api.getComparison().pipe(catchError(() => of(null)))
        : of(null);

    forkJoin({
      parameters: this.api.list(),
      comparison: comparison$,
    }).subscribe({
      next: ({ parameters, comparison }) => {
        this.parameters.set(parameters);
        if (comparison) {
          this.comparison.set(comparison);
        }
        this.drafts.set({});
        this.loading.set(false);
      },
      error: () => {
        this.error.set('loadFailed');
        this.loading.set(false);
      },
    });
  }

  row(key: SystemParameterKey): ParameterRow | undefined {
    return this.rows().get(key);
  }

  display(row: ParameterRow): string {
    if (row.parameter.locked || row.valueType === 'TIME' || row.valueType === 'STRING') {
      return row.text;
    }
    if (row.unit === 'vnd' && /^\d+$/.test(row.text)) {
      return this.i18n.formatNumber(Number(row.text));
    }
    return row.text;
  }

  groupMeta(group: ParameterGroup): string {
    const latest = group.keys
      .map((key) => this.row(key)?.parameter)
      .filter((parameter): parameter is SystemParameter => !!parameter?.updatedAt)
      .sort((a, b) => Date.parse(b.updatedAt ?? '') - Date.parse(a.updatedAt ?? ''))[0];
    if (!latest) return '';
    const date = this.i18n.formatDate(latest.updatedAt);
    return latest.updatedBy ? `${date} · ${latest.updatedBy}` : date;
  }

  errorText(row: ParameterRow): string {
    const { min, max } = row.parameter;
    const text = this.localization.systemParameters;
    switch (row.error) {
      case 'invalidPositive':
        return text.invalidPositive;
      case 'invalidTime':
        return text.invalidTime;
      case 'invalidDecimalRange':
        return this.i18n.translate('systemParameters.invalidDecimalRange', {
          min: min ?? 0,
          max: max ?? 100,
        });
      default:
        return this.i18n.translate('systemParameters.invalidRange', {
          min: min ?? 0,
          max: max ?? 0,
        });
    }
  }

  edit(key: SystemParameterKey, event: Event): void {
    const item = this.row(key);
    if (!item || item.parameter.locked) return;
    const input = event.target as HTMLInputElement;
    let cleaned = input.value;
    if (item.valueType === 'INTEGER') {
      cleaned = input.value.replace(/\D/g, '');
    } else if (item.valueType === 'DECIMAL' || item.valueType === 'PERCENT') {
      cleaned = input.value.replace(/,/g, '.').replace(/[^0-9.]/g, '');
    } else if (item.valueType === 'TIME') {
      cleaned = input.value.replace(/[^0-9:]/g, '');
    }
    this.saved.set(false);
    this.error.set(null);
    this.drafts.update((drafts) => ({ ...drafts, [key]: cleaned }));
    input.value = this.display({ ...item, text: cleaned });
  }

  toggleBoolean(key: SystemParameterKey, event: Event): void {
    const item = this.row(key);
    if (!item || item.parameter.locked) return;
    const checked = (event.target as HTMLInputElement).checked;
    this.saved.set(false);
    this.error.set(null);
    this.drafts.update((drafts) => ({ ...drafts, [key]: String(checked) }));
  }

  onBaselineChange(event: Event): void {
    const baseline = (event.target as HTMLSelectElement).value as EvaluationComparisonBaseline;
    this.comparisonSaved.set(false);
    this.comparison.update((c) => ({ ...c, baseline }));
  }

  onLookbackChange(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.comparisonSaved.set(false);
    this.comparison.update((c) => ({
      ...c,
      lookbackPeriods: Number.isFinite(value) ? Math.min(12, Math.max(2, Math.trunc(value))) : 3,
    }));
  }

  saveComparison(): void {
    if (typeof this.api.saveComparison !== 'function' || this.comparisonSaving()) return;
    this.comparisonSaving.set(true);
    this.comparisonSaved.set(false);
    this.api.saveComparison(this.comparison()).subscribe({
      next: (updated) => {
        this.comparison.set(updated);
        this.comparisonSaving.set(false);
        this.comparisonSaved.set(true);
      },
      error: () => {
        this.comparisonSaving.set(false);
        this.error.set('saveFailed');
      },
    });
  }

  discard(): void {
    this.drafts.set({});
    this.saved.set(false);
    this.error.set(null);
    this.confirming.set(false);
  }

  requestSave(): void {
    if (this.canSave()) this.confirming.set(true);
  }

  closeConfirm(): void {
    if (!this.saving()) this.confirming.set(false);
  }

  confirmSave(): void {
    if (!this.canSave()) return;
    const changes: SystemParameterChange[] = this.diffs().map(({ key, newValue }) => ({
      key,
      value: newValue,
    }));
    this.saving.set(true);
    this.error.set(null);
    this.api.save({ changes }).subscribe({
      next: (parameters) => {
        this.parameters.set(parameters);
        this.drafts.set({});
        this.saved.set(true);
        this.saving.set(false);
        this.confirming.set(false);
      },
      error: () => {
        this.error.set('saveFailed');
        this.saving.set(false);
        this.confirming.set(false);
      },
    });
  }

  valueText(value: number | string | boolean, key: SystemParameterKey): string {
    if (typeof value === 'boolean') {
      return value
        ? this.localization.systemParameters.boolOn
        : this.localization.systemParameters.boolOff;
    }
    const unit = UNITS[key];
    if (typeof value !== 'number') return value;
    const unitText = unit && unit !== 'time' ? this.localization.systemParameters.units[unit] : '';
    return `${this.i18n.formatNumber(value)} ${unitText}`.trim();
  }

  private resolveValueType(parameter: SystemParameter): SystemParameterValueType {
    if (parameter.valueType) return parameter.valueType;
    if (typeof parameter.value === 'boolean') return 'BOOLEAN';
    return (
      FALLBACK_TYPES[parameter.key] ?? (typeof parameter.value === 'string' ? 'STRING' : 'INTEGER')
    );
  }

  private parseWireValue(row: ParameterRow): number | string | boolean {
    if (row.valueType === 'BOOLEAN') return row.text === 'true';
    if (row.valueType === 'TIME' || row.valueType === 'STRING') {
      return row.text.length === 5 && /^\d{2}:\d{2}$/.test(row.text) ? `${row.text}:00` : row.text;
    }
    return Number(row.text);
  }

  private validate(
    parameter: SystemParameter,
    valueType: SystemParameterValueType,
    draft: string,
  ): ValidationErrorKind {
    if (parameter.locked) return null;
    if (valueType === 'BOOLEAN') {
      return draft === 'true' || draft === 'false' ? null : 'invalidRange';
    }
    if (valueType === 'TIME') {
      return /^([01]\d|2[0-3]):[0-5]\d(:[0-5]\d)?$/.test(draft.trim()) ? null : 'invalidTime';
    }
    const { min, max } = parameter;
    if (valueType === 'DECIMAL' || valueType === 'PERCENT') {
      const value = Number(draft);
      const valid =
        /^\d+(\.\d{1,4})?$/.test(draft) &&
        Number.isFinite(value) &&
        (min === null || value >= min) &&
        (max === null || value <= max);
      return valid ? null : 'invalidDecimalRange';
    }
    const value = Number(draft);
    const valid =
      /^\d+$/.test(draft) &&
      Number.isSafeInteger(value) &&
      (min === null || value >= min) &&
      (max === null || value <= max);
    if (valid) return null;
    return max === null || max >= Number.MAX_SAFE_INTEGER ? 'invalidPositive' : 'invalidRange';
  }
}
