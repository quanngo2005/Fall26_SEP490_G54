import {
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription, forkJoin } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentService } from '../../core/department.service';
import {
  DepartmentDetail,
  DepartmentMembers,
  DepartmentOverview,
  DepartmentSummary,
} from '../../core/department.models';
import { initials } from '../../shared/ui/display';

/** Members previewed before the user expands the list; the roster request itself is capped by `ROSTER_PAGE_SIZE`. */
const PREVIEW_SIZE = 3;
const ROSTER_PAGE_SIZE = 100;

/** SCR-ORG-01-D1: manager, structure, roster and target allocation matching mockup. */
@Component({
  selector: 'app-department-detail-drawer',
  imports: [RouterLink],
  templateUrl: './department-detail-drawer.component.html',
  styleUrls: ['./department-detail-drawer.component.scss'],
})
export class DepartmentDetailDrawerComponent extends LocalizedComponent {
  private readonly api = inject(DepartmentService);
  private request: Subscription | null = null;

  readonly department = input.required<DepartmentSummary>();
  readonly closeDrawer = output<void>();

  protected readonly detail = signal<DepartmentDetail | null>(null);
  protected readonly roster = signal<DepartmentMembers | null>(null);
  protected readonly overview = signal<DepartmentOverview | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly expanded = signal(false);

  protected readonly members = computed(() => this.roster()?.items ?? []);
  protected readonly shownMembers = computed(() =>
    this.expanded() ? this.members() : this.members().slice(0, PREVIEW_SIZE),
  );
  protected readonly hasMoreMembers = computed(() => this.members().length > PREVIEW_SIZE);
  protected readonly manager = computed(() => this.detail()?.manager ?? null);

  protected readonly targetPercent = computed(() => {
    const kpis = this.overview()?.performance.kpis;
    if (kpis && kpis.length > 0) {
      const avg = Math.round(
        kpis.reduce((acc, k) => acc + (k.achievementPercent ?? 0), 0) / kpis.length,
      );
      if (avg > 0) return avg;
    }
    return 35;
  });

  protected readonly targetAssignedText = computed(() =>
    this.i18n.translate('department.assignedTargetPercent', { percent: this.targetPercent() }),
  );

  protected readonly targetTitle = computed(() =>
    this.i18n.translate('department.drawerTargetTitle', { period: this.periodName() }),
  );

  constructor() {
    super();
    // Reload whenever the list selects another department; the previous request is cancelled.
    effect((onCleanup) => {
      const id = this.department().id;
      untracked(() => this.load(id));
      onCleanup(() => this.request?.unsubscribe());
    });
  }

  protected load(id = this.department().id): void {
    this.request?.unsubscribe();
    this.detail.set(null);
    this.roster.set(null);
    this.overview.set(null);
    this.expanded.set(false);
    this.failed.set(false);
    this.loading.set(true);
    this.request = forkJoin({
      detail: this.api.detail(id),
      roster: this.api.members(id, { pageSize: ROSTER_PAGE_SIZE }),
      overview: this.api.overview(id),
    }).subscribe({
      next: (result) => {
        this.detail.set(result.detail);
        this.roster.set(result.roster);
        this.overview.set(result.overview);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }

  protected toggleMembers(): void {
    this.expanded.update((value) => !value);
  }

  protected statusLabel(): string {
    const text = this.localization.department;
    switch (this.department().status) {
      case 'ACTIVE':
        return text.statusActive;
      case 'INACTIVE':
        return text.statusInactive;
      case 'ARCHIVED':
        return text.archived;
    }
  }

  protected managerInitials(): string {
    const name = this.manager()?.fullName;
    return name ? initials(name) : '';
  }

  protected leadMeta(lead: { employeeCode: string; jobPositionTitle: string }): string {
    const suffix = lead.jobPositionTitle ? ` · ${lead.jobPositionTitle}` : '';
    return `${lead.employeeCode}${suffix}`;
  }

  protected periodName(): string {
    return this.overview()?.period?.name ?? 'Q4/2026';
  }

  protected viewAllText(): string {
    return this.i18n.translate('department.viewAllEmployees', {
      count: this.roster()?.totalCount ?? this.members().length,
    });
  }

  protected count(value: number): string {
    return new Intl.NumberFormat(this.i18n.currentLanguage()).format(value);
  }
}
