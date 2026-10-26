import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { catchError, EMPTY, finalize, Subject, switchMap } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  KPI_AGGREGATIONS,
  KPI_DIRECTIONS,
  KpiDefinition,
  KpiDefinitionSavePayload,
  KpiGroupOption,
} from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { KpiCreateModalComponent } from './kpi-create-modal/kpi-create-modal.component';
import { KpiEditModalComponent } from './kpi-edit-modal/kpi-edit-modal.component';

export interface DraftRow {
  key: number;
  label: string;
  value: number;
}

interface Notice {
  kind: 'success' | 'error';
  key: string;
  params?: Record<string, string | number>;
}

interface KpiDrafts {
  roles: DraftRow[];
  multipliers: DraftRow[];
}

const PAGE_SIZE = 20;
const NOTICE_MS = 5000;

@Component({
  selector: 'app-kpi-definitions',
  imports: [
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    KpiCreateModalComponent,
    KpiEditModalComponent,
  ],
  templateUrl: './kpi-definitions.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    '../../shared/ui/modal.scss',
    './kpi-definitions.component.scss',
  ],
})
export class KpiDefinitionsComponent extends LocalizedComponent {
  private readonly api = inject(KpiDefinitionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  /** Session-only drafts per KPI. They are never sent to the API (docs/api/KPI_DEFINITION_CONTRACT.md). */
  private readonly drafts = new Map<string, KpiDrafts>();
  private draftKey = 0;
  private noticeTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly directions = KPI_DIRECTIONS;
  protected readonly aggregations = KPI_AGGREGATIONS;

  protected readonly kpis = signal<KpiDefinition[]>([]);
  protected readonly groups = signal<KpiGroupOption[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly selected = signal<KpiDefinition | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly saving = signal(false);
  protected readonly notice = signal<Notice | null>(null);
  protected readonly createOpen = signal(false);
  protected readonly editOpen = signal(false);
  protected readonly roleRows = signal<DraftRow[]>([]);
  protected readonly multiplierRows = signal<DraftRow[]>([]);

  protected keyword = '';
  protected groupId = '';
  protected page = 1;
  protected form = this.emptyForm();

  protected readonly unitSuggestions = computed(() =>
    [
      ...new Set(
        this.kpis()
          .map((kpi) => kpi.unit)
          .filter(Boolean),
      ),
    ].sort(),
  );
  protected readonly roleTotal = computed(() =>
    this.roleRows().reduce((sum, row) => sum + (Number(row.value) || 0), 0),
  );

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          return this.api
            .list({
              keyword: this.keyword.trim(),
              kpiGroupId: this.groupId,
              page: this.page,
              pageSize: PAGE_SIZE,
            })
            .pipe(
              catchError(() => {
                this.failed.set(true);
                return EMPTY;
              }),
              finalize(() => this.loading.set(false)),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((page) => {
        this.kpis.set(page.items);
        this.totalCount.set(page.totalCount);
        const current = this.selected();
        this.select(page.items.find((kpi) => kpi.id === current?.id) ?? page.items[0] ?? null);
      });

    this.api
      .groups()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (groups) => this.groups.set(groups),
        error: () => this.show('error', 'kpi.loadFailed'),
      });
    this.destroyRef.onDestroy(() => this.noticeTimer && clearTimeout(this.noticeTimer));
    this.refresh();
  }

  protected refresh(resetPage = false): void {
    if (resetPage) this.page = 1;
    this.reload.next();
  }

  protected resetFilters(): void {
    this.keyword = '';
    this.groupId = '';
    this.refresh(true);
  }

  protected turnPage(delta: number): void {
    this.page += delta;
    this.refresh();
  }

  protected totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE));
  }

  protected select(kpi: KpiDefinition | null): void {
    this.stashDrafts();
    this.selected.set(kpi);
    this.form = kpi ? this.formOf(kpi) : this.emptyForm();
    const draft = kpi ? this.drafts.get(kpi.id) : undefined;
    this.roleRows.set(draft ? draft.roles.map((row) => ({ ...row })) : []);
    this.multiplierRows.set(draft ? draft.multipliers.map((row) => ({ ...row })) : []);
  }

  protected isDirty(): boolean {
    const kpi = this.selected();
    if (!kpi) return false;
    const saved = this.formOf(kpi);
    return (Object.keys(saved) as (keyof typeof saved)[]).some(
      (key) => saved[key] !== this.form[key],
    );
  }

  protected cancelChanges(): void {
    const kpi = this.selected();
    if (!kpi) return;
    this.form = this.formOf(kpi);
    this.show('success', 'kpi.reverted');
  }

  protected save(): void {
    const kpi = this.selected();
    if (!kpi || this.saving()) return;
    if (!this.form.name.trim()) {
      this.show('error', 'kpi.nameRequired');
      return;
    }
    if (!this.form.unit.trim()) {
      this.show('error', 'kpi.unitRequired');
      return;
    }
    const payload: KpiDefinitionSavePayload = {
      kpiGroupId: this.form.kpiGroupId,
      code: kpi.code,
      name: this.form.name.trim(),
      description: this.form.description.trim() || null,
      formula: this.form.formula.trim() || null,
      unit: this.form.unit.trim(),
      aggregationType: this.form.aggregationType,
      direction: this.form.direction,
      maxAchievementPercent: this.parseMax(this.form.maxAchievementPercent),
    };
    this.saving.set(true);
    this.api
      .update(kpi.id, payload)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (updated) => {
          this.kpis.update((list) => list.map((item) => (item.id === updated.id ? updated : item)));
          this.selected.set(updated);
          this.form = this.formOf(updated);
          this.show('success', 'kpi.saved');
        },
        error: (error: unknown) =>
          this.show(
            'error',
            error instanceof HttpErrorResponse && error.status === 404
              ? 'kpi.notFound'
              : 'kpi.saveFailed',
          ),
      });
  }

  protected openCreate(): void {
    this.createOpen.set(true);
  }

  protected closeCreate(): void {
    this.createOpen.set(false);
  }

  protected onCreated(created: KpiDefinition): void {
    this.createOpen.set(false);
    this.show('success', 'kpi.created', { code: created.code });
    this.keyword = '';
    this.groupId = '';
    this.page = 1;
    this.select(created);
    this.refresh();
  }

  protected openEdit(): void {
    if (this.selected()) {
      this.editOpen.set(true);
    }
  }

  protected closeEdit(): void {
    this.editOpen.set(false);
  }

  protected onEdited(updated: KpiDefinition): void {
    this.editOpen.set(false);
    this.kpis.update((list) => list.map((item) => (item.id === updated.id ? updated : item)));
    this.selected.set(updated);
    this.form = this.formOf(updated);
    this.show('success', 'kpi.saved');
  }

  protected dismissNotice(): void {
    this.notice.set(null);
  }

  protected noticeText(): string {
    const notice = this.notice();
    return notice ? this.i18n.translate(notice.key, notice.params) : '';
  }

  protected directionLabel(direction: string): string {
    const text = this.localization.kpi;
    switch (direction) {
      case 'HIGHER_BETTER':
        return text.higherBetter;
      case 'LOWER_BETTER':
        return text.lowerBetter;
      case 'ON_TARGET':
        return text.onTarget;
      default:
        return text.notAvailable;
    }
  }

  protected aggregationLabel(aggregation: string): string {
    const text = this.localization.kpi;
    switch (aggregation) {
      case 'SUM':
        return text.sum;
      case 'RATIO':
        return text.ratio;
      case 'AVG':
        return text.average;
      case 'NO_AGGREGATION':
        return text.noAggregation;
      default:
        return text.notAvailable;
    }
  }

  protected statusLabel(status: string | null): string {
    const text = this.localization.kpi;
    switch (status) {
      case 'DRAFT':
        return text.statusDraft;
      case 'ACTIVE':
        return text.statusActive;
      case 'INACTIVE':
        return text.statusInactive;
      case 'ARCHIVED':
        return text.statusArchived;
      default:
        return text.notAvailable;
    }
  }

  protected addRow(target: 'roles' | 'multipliers'): void {
    const row: DraftRow = { key: ++this.draftKey, label: '', value: target === 'roles' ? 0 : 1 };
    const rows = target === 'roles' ? this.roleRows : this.multiplierRows;
    rows.update((list) => [...list, row]);
  }

  protected removeRow(target: 'roles' | 'multipliers', key: number): void {
    const rows = target === 'roles' ? this.roleRows : this.multiplierRows;
    rows.update((list) => list.filter((row) => row.key !== key));
  }

  protected editRow(
    target: 'roles' | 'multipliers',
    key: number,
    patch: Partial<Pick<DraftRow, 'label' | 'value'>>,
  ): void {
    const rows = target === 'roles' ? this.roleRows : this.multiplierRows;
    rows.update((list) => list.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  }

  protected rowLabel(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected rowValue(event: Event): number {
    return Number((event.target as HTMLInputElement).value) || 0;
  }

  private stashDrafts(): void {
    const previous = this.selected();
    if (!previous) return;
    this.drafts.set(previous.id, { roles: this.roleRows(), multipliers: this.multiplierRows() });
  }

  private show(kind: Notice['kind'], key: string, params?: Notice['params']): void {
    this.notice.set({ kind, key, params });
    if (this.noticeTimer) clearTimeout(this.noticeTimer);
    this.noticeTimer = setTimeout(() => this.notice.set(null), NOTICE_MS);
  }

  private parseMax(value: number | null | string): number | null {
    if (value === null || value === '') return null;
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  private formOf(kpi: KpiDefinition) {
    return {
      name: kpi.name,
      kpiGroupId: kpi.kpiGroupId,
      description: kpi.description ?? '',
      unit: kpi.unit,
      direction: kpi.direction,
      aggregationType: kpi.aggregationType,
      formula: kpi.formula ?? '',
      maxAchievementPercent: kpi.maxAchievementPercent as number | null | string,
    };
  }

  private emptyForm() {
    return {
      name: '',
      kpiGroupId: '',
      description: '',
      unit: '',
      direction: 'HIGHER_BETTER' as KpiDefinition['direction'],
      aggregationType: 'SUM' as KpiDefinition['aggregationType'],
      formula: '',
      maxAchievementPercent: null as number | null | string,
    };
  }
}
