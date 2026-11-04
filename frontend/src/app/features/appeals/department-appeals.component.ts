import { UpperCasePipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { catchError, EMPTY, finalize, forkJoin, of, Subject, switchMap, tap } from 'rxjs';
import { AppealListItem, AppealStatus, AppealType } from '../../core/appeal.models';
import { AppealService } from '../../core/appeal.service';
import { EmployeeJobPositionOption } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe } from '../../shared/pipes';
import {
  appealCode,
  appealSubject,
  OPEN_STATUSES,
  RESOLVED_STATUSES,
  typeKey,
} from './appeal-display';

type DepartmentTab = 'NEEDS_ACTION' | 'RESOLVED' | 'ALL';

const TAB_STATUSES: Readonly<Record<DepartmentTab, readonly AppealStatus[]>> = {
  NEEDS_ACTION: OPEN_STATUSES,
  RESOLVED: RESOLVED_STATUSES,
  ALL: [],
};

@Component({
  selector: 'app-department-appeals',
  imports: [
    FormsModule,
    RouterModule,
    SidebarComponent,
    HeaderComponent,
    AppDatePipe,
    UpperCasePipe,
  ],
  templateUrl: './department-appeals.component.html',
  styleUrls: ['./appeals-shared.scss', './department-appeals.component.scss'],
})
export class DepartmentAppealsComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(AppealService);
  private readonly evaluations = inject(EvaluationService);
  private readonly employees = inject(EmployeeService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  protected readonly activeTab = signal<DepartmentTab>('NEEDS_ACTION');
  protected readonly searchQuery = signal('');
  protected readonly typeFilter = signal<AppealType | 'ALL'>('ALL');
  protected readonly periodFilter = signal('ALL');
  protected readonly positionFilter = signal('ALL');
  protected readonly page = signal(1);

  protected readonly items = signal<AppealListItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly pageSize = signal(0);
  protected readonly counts = signal({ needsAction: 0, resolved: 0 });
  protected readonly periods = signal<EvaluationPeriodOption[]>([]);
  protected readonly positions = signal<EmployeeJobPositionOption[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly filteredItems = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    if (!query) return this.items();
    return this.items().filter((item) => {
      const text =
        `${appealCode(item.id)} ${item.employeeName} ${item.employeeCode} ${appealSubject(item) ?? ''}`.toLowerCase();
      return text.includes(query);
    });
  });

  protected readonly pages = computed(() => {
    const size = this.pageSize();
    return size > 0
      ? Array.from({ length: Math.ceil(this.totalCount() / size) }, (_, i) => i + 1)
      : [];
  });

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          const base = { scope: 'TEAM' as const, ...this.baseFilter() };
          return forkJoin({
            list: this.api.list({
              ...base,
              status: TAB_STATUSES[this.activeTab()],
              page: this.page(),
            }),
            needsAction: this.api.list({ ...base, status: OPEN_STATUSES, pageSize: 1 }),
            resolved: this.api.list({ ...base, status: RESOLVED_STATUSES, pageSize: 1 }),
          }).pipe(
            tap(({ list, needsAction, resolved }) => {
              this.items.set(list.items);
              this.totalCount.set(list.totalCount);
              this.pageSize.set(list.pageSize);
              this.counts.set({
                needsAction: needsAction.totalCount,
                resolved: resolved.totalCount,
              });
            }),
            catchError(() => {
              this.failed.set(true);
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  ngOnInit(): void {
    // The period and position filters are supplementary: the list must still work without them.
    this.evaluations
      .periods()
      .pipe(
        catchError(() => of([])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((periods) => this.periods.set(periods));
    this.employees
      .jobPositions()
      .pipe(
        catchError(() => of([])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((positions) => this.positions.set(positions));
    this.reload.next();
  }

  protected retry(): void {
    this.reload.next();
  }

  protected setTab(tab: DepartmentTab): void {
    this.activeTab.set(tab);
    this.page.set(1);
    this.reload.next();
  }

  protected setType(type: AppealType | 'ALL'): void {
    this.typeFilter.set(type);
    this.page.set(1);
    this.reload.next();
  }

  protected setPeriod(periodId: string): void {
    this.periodFilter.set(periodId);
    this.page.set(1);
    this.reload.next();
  }

  protected setPosition(positionId: string): void {
    this.positionFilter.set(positionId);
    this.page.set(1);
    this.reload.next();
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.reload.next();
  }

  protected viewDetail(item: AppealListItem): void {
    void this.router.navigate(['/appeals/department', item.id]);
  }

  protected statusLabel(status: AppealStatus): string {
    return this.localization.appeals.status[status];
  }

  protected kindLabel(type: AppealType): string {
    return this.localization.appeals[typeKey(type)];
  }

  protected subject(item: AppealListItem): string {
    return appealSubject(item) ?? this.localization.appeals.notAvailable;
  }

  protected code(item: AppealListItem): string {
    return appealCode(item.id);
  }

  protected showingText(): string {
    const total = this.totalCount();
    const from = total === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1;
    return this.i18n.translate('appeals.showing', {
      from,
      to: from === 0 ? 0 : from + this.items().length - 1,
      total,
    });
  }

  private baseFilter(): { type?: AppealType; periodId?: string; jobPositionId?: string } {
    const type = this.typeFilter();
    const period = this.periodFilter();
    const position = this.positionFilter();
    return {
      ...(type !== 'ALL' ? { type } : {}),
      ...(period !== 'ALL' ? { periodId: period } : {}),
      ...(position !== 'ALL' ? { jobPositionId: position } : {}),
    };
  }
}
