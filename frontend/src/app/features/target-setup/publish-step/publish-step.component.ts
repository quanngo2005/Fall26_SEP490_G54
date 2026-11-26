import {
  Component,
  computed,
  DestroyRef,
  EventEmitter,
  inject,
  Input,
  OnChanges,
  Output,
  signal,
  SimpleChanges,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DepartmentSummary } from '../../../core/department.models';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { KpiAllocation, TargetSetup } from '../../../core/target-assignment.models';
import { TargetAssignmentService } from '../../../core/target-assignment.service';
import { numberLocale } from '../target-math';

interface PublishDepartmentRow {
  departmentId: string;
  departmentName: string;
  managerName: string | null;
  kpiCount: number;
  /** Mean % change against last year's target over KPIs that had one; null without any history. */
  changePercent: number | null;
}

/**
 * W7 (design/v3/w07-publish.png): last wizard step. Summary cards, one row per department receiving targets
 * (manager from the department list; KPIs received and the average change against last year from the tier-2
 * allocations) and "Phát hành cho N phòng". Not shown because the contract has no data for them: a note to the
 * department managers (publish takes no body), the over-ceiling ±X% count and the suggested / manual cell count
 * (suggestion provenance is not persisted).
 */
@Component({
  selector: 'app-publish-step',
  templateUrl: './publish-step.component.html',
  styleUrls: ['../../../shared/ui/ui-controls.scss', './publish-step.component.scss'],
})
export class PublishStepComponent extends LocalizedComponent implements OnChanges {
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly destroyRef = inject(DestroyRef);

  @Input({ required: true }) branchYearId!: string;
  @Input({ required: true }) year!: number;
  @Input({ required: true }) set setup(value: TargetSetup) {
    this.setupState.set(value);
  }
  @Input() set departments(value: readonly DepartmentSummary[]) {
    this.managers.set(new Map(value.map((d) => [d.id, d.managerName])));
  }
  @Input() publishing = false;
  @Input() error: string | null = null;
  @Output() readonly back = new EventEmitter<void>();
  @Output() readonly publish = new EventEmitter<void>();

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly setupState = signal<TargetSetup | null>(null);
  private readonly allocations = signal<readonly KpiAllocation[]>([]);
  private readonly managers = signal<ReadonlyMap<string, string | null>>(new Map());

  protected readonly rows = computed<PublishDepartmentRow[]>(() => {
    const byId = new Map<string, { row: PublishDepartmentRow; changes: number[] }>();
    for (const allocation of this.allocations()) {
      for (const department of allocation.departments) {
        if (department.year === 0) {
          continue;
        }
        let entry = byId.get(department.departmentId);
        if (!entry) {
          entry = {
            row: {
              departmentId: department.departmentId,
              departmentName: department.departmentName,
              managerName: this.managers().get(department.departmentId) ?? null,
              kpiCount: 0,
              changePercent: null,
            },
            changes: [],
          };
          byId.set(department.departmentId, entry);
        }
        entry.row.kpiCount += 1;
        if (department.previousYear) {
          entry.changes.push(
            ((department.year - department.previousYear) / department.previousYear) * 100,
          );
        }
      }
    }
    return [...byId.values()].map(({ row, changes }) => ({
      ...row,
      changePercent: changes.length
        ? changes.reduce((sum, value) => sum + value, 0) / changes.length
        : null,
    }));
  });

  protected readonly published = computed(() => this.setupState()?.step === 'PUBLISHED');

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['branchYearId']) {
      this.load();
    }
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.targetApi
      .getAllAllocations(this.branchYearId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (allocations) => {
          this.allocations.set(allocations);
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  /** "28 · 26 phân bổ · 2 kế thừa": every KPI, the allocatable ones and the branch-wide (inherited) ones. */
  protected kpiSummary(): string {
    const setup = this.setupState();
    const total = setup?.quarters.total ?? 0;
    const allocated = setup?.allocation.total ?? 0;
    return this.localization.publishStep.kpiSummary
      .replace('{total}', `${total}`)
      .replace('{allocated}', `${allocated}`)
      .replace('{inherited}', `${Math.max(0, total - allocated)}`);
  }

  protected changeHeader(): string {
    return this.localization.publishStep.columnChange.replace('{year}', `${this.year - 1}`);
  }

  protected noHistoryNote(): string {
    return this.localization.publishStep.noHistory.replace('{year}', `${this.year - 1}`);
  }

  protected changeText(percent: number | null): string {
    if (percent === null) {
      return '—';
    }
    const formatted = new Intl.NumberFormat(numberLocale(this.i18n.currentLanguage()), {
      minimumFractionDigits: 1,
      maximumFractionDigits: 1,
      signDisplay: 'always',
    }).format(percent);
    return `${formatted}%`;
  }

  protected publishLabel(): string {
    return this.localization.publishStep.publishButton.replace('{count}', `${this.rows().length}`);
  }
}
