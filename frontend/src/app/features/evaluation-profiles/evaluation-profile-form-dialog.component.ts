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
import { forkJoin, of } from 'rxjs';
import { EmployeeJobPositionOption } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationProfileDetail } from '../../core/evaluation-profile.models';
import { EvaluationProfileService } from '../../core/evaluation-profile.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { serverMessage } from '../campaigns/campaign.utils';
import { evaluationPeriodLabel } from './evaluation-profile-period';
import {
  ProfileRow,
  buildItems,
  countRowChanges,
  flattenItems,
  totalWeight,
} from './evaluation-profile-tree';

interface KpiChoice {
  id: string;
  code: string;
  name: string;
}

const MAX_CODE = 50;
const MAX_NAME = 200;

/** SCR-CFG-01-M1: create an evaluation profile, or edit one (name and KPI weights). */
@Component({
  selector: 'app-evaluation-profile-form-dialog',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './evaluation-profile-form-dialog.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/modal.scss',
    './evaluation-profile-form-dialog.component.scss',
  ],
})
export class EvaluationProfileFormDialogComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(EvaluationProfileService);
  private readonly employeeApi = inject(EmployeeService);
  private readonly kpiApi = inject(KpiDefinitionService);
  private readonly destroyRef = inject(DestroyRef);

  /** null creates a new profile; an id edits that profile. */
  readonly profileId = input<string | null>(null);
  readonly saved = output<void>();
  readonly dismissed = output<void>();

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly detail = signal<EvaluationProfileDetail | null>(null);
  protected readonly positions = signal<EmployeeJobPositionOption[]>([]);
  protected readonly kpis = signal<KpiChoice[]>([]);
  protected readonly rows = signal<ProfileRow[]>([]);
  /** False when the stored tree cannot be shown as KPI rows, so saving would change its shape. */
  protected readonly treeSupported = signal(true);

  protected code = '';
  protected name = '';
  protected jobPositionId = '';
  protected effectiveFrom = '';
  protected effectiveTo = '';

  private originalRows: ProfileRow[] = [];

  protected get isEdit(): boolean {
    return this.profileId() !== null;
  }

  /** A profile already used by an evaluation only accepts a new name (API: EVALUATION_PROFILE_IN_USE). */
  protected get structureLocked(): boolean {
    return this.detail()?.isInUse ?? false;
  }

  ngOnInit(): void {
    const id = this.profileId();
    forkJoin({
      kpis: this.kpiApi.list({ page: 1, pageSize: 100 }),
      positions: id ? of(null) : this.employeeApi.jobPositions(),
      detail: id ? this.api.detail(id) : of(null),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ kpis, positions, detail }) => {
          const choices = kpis.items
            .filter((kpi) => kpi.status === 'ACTIVE')
            .map(({ id: kpiId, code, name }) => ({ id: kpiId, code, name }));
          if (detail) this.applyDetail(detail, choices);
          else this.kpis.set(choices);
          if (positions) this.positions.set(positions);
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  private applyDetail(detail: EvaluationProfileDetail, active: KpiChoice[]): void {
    const flat = flattenItems(detail.items);
    // A KPI already in the profile stays selectable even if it was deactivated since (API contract).
    const known = new Set(active.map((kpi) => kpi.id));
    const stored = detail.items
      .flatMap((root) => root.children)
      .filter((leaf) => leaf.kpiDefinitionId && !known.has(leaf.kpiDefinitionId))
      .map((leaf) => ({
        id: leaf.kpiDefinitionId as string,
        code: leaf.kpiCode ?? '',
        name: leaf.kpiName ?? '',
      }));
    this.kpis.set([...active, ...stored]);
    this.detail.set(detail);
    this.name = detail.name;
    this.jobPositionId = detail.jobPositionId;
    this.effectiveFrom = detail.effectiveFrom;
    this.effectiveTo = detail.effectiveTo ?? '';
    this.originalRows = flat.rows;
    this.rows.set(flat.rows.map((row) => ({ ...row })));
    this.treeSupported.set(flat.supported);
  }

  protected metaText(): string {
    const detail = this.detail();
    if (!detail) return 'SCR-CFG-01-M1';
    return `SCR-CFG-01-M1 · ${detail.name} · ${this.periodText()}`;
  }

  protected periodText(): string {
    const detail = this.detail();
    if (!detail) return '';
    return evaluationPeriodLabel(
      detail.effectiveFrom,
      detail.effectiveTo,
      (value) => this.i18n.formatDate(value, 'DD/MM/YYYY'),
      this.localization.evaluationProfiles.openEnded,
    );
  }

  protected positionTitle(): string {
    return this.detail()?.jobPositionTitle ?? '';
  }

  protected addRow(): void {
    this.rows.update((rows) => [...rows, { group: '', kpiDefinitionId: '', weight: null }]);
  }

  protected removeRow(index: number): void {
    this.rows.update((rows) => rows.filter((_, i) => i !== index));
  }

  protected setGroup(index: number, group: string): void {
    this.patchRow(index, { group });
  }

  protected setKpi(index: number, kpiDefinitionId: string): void {
    this.patchRow(index, { kpiDefinitionId });
  }

  protected setWeight(index: number, weight: number | null): void {
    this.patchRow(index, {
      weight: weight === null || Number.isNaN(Number(weight)) ? null : Number(weight),
    });
  }

  /** KPIs not yet used by another row, plus the one already chosen in this row. */
  protected kpiOptions(index: number): KpiChoice[] {
    const used = new Set(
      this.rows()
        .filter((_, i) => i !== index)
        .map((row) => row.kpiDefinitionId),
    );
    return this.kpis().filter((kpi) => !used.has(kpi.id));
  }

  protected total(): number {
    return totalWeight(this.rows());
  }

  protected changeText(): string {
    const nameChanged = this.isEdit && this.name.trim() !== (this.detail()?.name ?? '') ? 1 : 0;
    const rowChanges = this.isEdit
      ? countRowChanges(this.originalRows, this.rows())
      : this.rows().length;
    return this.i18n.translate('evaluationProfileForm.changes', {
      name: nameChanged,
      rows: rowChanges,
    });
  }

  protected rowInvalid(row: ProfileRow): boolean {
    return (
      !row.group.trim() ||
      !row.kpiDefinitionId ||
      row.weight === null ||
      !(row.weight > 0 && row.weight <= 100)
    );
  }

  protected periodInvalid(): boolean {
    return !!this.effectiveTo && !!this.effectiveFrom && this.effectiveTo < this.effectiveFrom;
  }

  protected valid(): boolean {
    const name = this.name.trim();
    if (!name || name.length > MAX_NAME) return false;
    if (!this.isEdit) {
      const code = this.code.trim();
      if (!code || code.length > MAX_CODE || !this.jobPositionId || !this.effectiveFrom)
        return false;
    }
    if (this.periodInvalid()) return false;
    if (!this.treeSupported()) return false;
    return !this.rows().some((row) => this.rowInvalid(row));
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.close();
  }

  @HostListener('document:keydown.control.s', ['$event'])
  protected onSaveShortcut(event: Event): void {
    event.preventDefault();
    this.save();
  }

  protected close(): void {
    if (!this.saving()) this.dismissed.emit();
  }

  protected save(): void {
    if (this.saving() || this.loading() || this.loadFailed() || !this.valid()) return;
    this.saving.set(true);
    this.saveError.set(null);
    const body = {
      name: this.name.trim(),
      description: this.detail()?.description ?? null,
      jobPositionId: this.jobPositionId,
      effectiveFrom: this.effectiveFrom,
      effectiveTo: this.effectiveTo || null,
      items: buildItems(this.rows()),
    };
    const id = this.profileId();
    const request = id
      ? this.api.update(id, body)
      : this.api.create({ ...body, code: this.code.trim() });
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.saving.set(false);
        this.saved.emit();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.saveError.set(
          serverMessage(error) ?? this.localization.evaluationProfileForm.saveError,
        );
      },
    });
  }

  private patchRow(index: number, patch: Partial<ProfileRow>): void {
    this.rows.update((rows) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }
}
