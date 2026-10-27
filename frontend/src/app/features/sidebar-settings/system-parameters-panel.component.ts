import { Component, computed, inject, signal } from '@angular/core';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { SystemParameterService } from '../../core/system-parameter.service';
import {
  SystemParameter,
  SystemParameterChange,
  SystemParameterKey,
} from '../../core/system-parameter.models';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { ConfirmSaveDialogComponent } from './confirm-save-dialog.component';

type GroupId = 'manualApproval' | 'importData' | 'evaluationAppeal' | 'handover';
type UnitId = 'hours' | 'megabytes' | 'days' | 'vnd';

interface ParameterGroup {
  id: GroupId;
  keys: SystemParameterKey[];
}

interface ParameterRow {
  parameter: SystemParameter;
  text: string;
  unit: UnitId | null;
  changed: boolean;
  error: 'invalidRange' | 'invalidPositive' | null;
}

export interface ParameterDiff {
  key: SystemParameterKey;
  oldValue: number | string;
  newValue: number;
}

const COLUMNS: readonly (readonly ParameterGroup[])[] = [
  [
    { id: 'manualApproval', keys: ['manualApprovalHours'] },
    { id: 'importData', keys: ['importMaxFileMb', 'importFormats', 'kpiLockTime'] },
  ],
  [
    {
      id: 'evaluationAppeal',
      keys: ['appealWindowDays', 'preCloseCheckDays', 'appealRejectedHours'],
    },
    { id: 'handover', keys: ['handoverThresholdVnd'] },
  ],
];

const UNITS: Readonly<Partial<Record<SystemParameterKey, UnitId>>> = {
  manualApprovalHours: 'hours',
  importMaxFileMb: 'megabytes',
  appealWindowDays: 'days',
  preCloseCheckDays: 'days',
  appealRejectedHours: 'hours',
  handoverThresholdVnd: 'vnd',
};

/** SCR-ADM-04, tab "Tham số vận hành". Save/discard are driven by the page title bar. */
@Component({
  selector: 'app-system-parameters-panel',
  imports: [LayoutIconComponent, ConfirmSaveDialogComponent],
  templateUrl: './system-parameters-panel.component.html',
  styleUrls: ['./system-parameters-panel.component.scss', './diff-table.scss'],
})
export class SystemParametersPanelComponent extends LocalizedComponent {
  private readonly api = inject(SystemParameterService);

  readonly columns = COLUMNS;
  readonly parameters = signal<SystemParameter[]>([]);
  /** Raw digits typed by the admin, keyed by parameter; absent means unchanged. */
  readonly drafts = signal<Partial<Record<SystemParameterKey, string>>>({});
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<'loadFailed' | 'saveFailed' | null>(null);
  readonly saved = signal(false);
  readonly confirming = signal(false);

  readonly rows = computed(() => {
    const drafts = this.drafts();
    return new Map<SystemParameterKey, ParameterRow>(
      this.parameters().map((parameter) => {
        const draft = drafts[parameter.key];
        const changed = draft !== undefined && draft !== String(parameter.value);
        return [
          parameter.key,
          {
            parameter,
            text: draft ?? String(parameter.value),
            unit: UNITS[parameter.key] ?? null,
            changed,
            error: changed ? this.validate(parameter, draft) : null,
          },
        ];
      }),
    );
  });
  readonly diffs = computed<ParameterDiff[]>(() =>
    [...this.rows().values()]
      .filter((row) => row.changed && !row.error)
      .map((row) => ({
        key: row.parameter.key,
        oldValue: row.parameter.value,
        newValue: Number(row.text),
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

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.list().subscribe({
      next: (parameters) => {
        this.parameters.set(parameters);
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

  /** Display text: grouped digits for the VND amount, raw otherwise. */
  display(row: ParameterRow): string {
    return row.parameter.locked || !/^\d+$/.test(row.text) || row.unit !== 'vnd'
      ? row.text
      : this.i18n.formatNumber(Number(row.text));
  }

  /** Latest edit among the group's parameters, e.g. "12/10/2026 · sa.admin". */
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
    return row.error === 'invalidPositive'
      ? text.invalidPositive
      : this.i18n.translate('systemParameters.invalidRange', { min: min ?? 0, max: max ?? 0 });
  }

  edit(key: SystemParameterKey, event: Event): void {
    const input = event.target as HTMLInputElement;
    const digits = input.value.replace(/\D/g, '');
    this.saved.set(false);
    this.error.set(null);
    this.drafts.update((drafts) => ({ ...drafts, [key]: digits }));
    const row = this.row(key);
    input.value = row ? this.display({ ...row, text: digits }) : digits;
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

  valueText(value: number | string, key: SystemParameterKey): string {
    const unit = UNITS[key];
    if (typeof value !== 'number') return value;
    const unitText = unit ? this.localization.systemParameters.units[unit] : '';
    return `${this.i18n.formatNumber(value)} ${unitText}`.trim();
  }

  private validate(parameter: SystemParameter, draft: string): ParameterRow['error'] {
    if (parameter.locked) return null;
    const value = Number(draft);
    const { min, max } = parameter;
    const valid =
      /^\d+$/.test(draft) &&
      Number.isSafeInteger(value) &&
      (min === null || value >= min) &&
      (max === null || value <= max);
    if (valid) return null;
    return max === null || max >= Number.MAX_SAFE_INTEGER ? 'invalidPositive' : 'invalidRange';
  }
}
