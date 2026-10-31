import { Component, DestroyRef, HostListener, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { catchError, EMPTY, finalize, Subject, switchMap } from 'rxjs';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentService } from '../../core/department.service';
import {
  DepartmentFilter,
  DepartmentStatus,
  DepartmentSummary,
} from '../../core/department.models';
import { displayNumber } from '../../shared/ui/display';
import { DepartmentDetailDrawerComponent } from './department-detail-drawer.component';

/** SCR-ORG-01: read-only department list matching mockup with the SCR-ORG-01-D1 detail drawer. */
@Component({
  selector: 'app-department-list',
  imports: [
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    DepartmentDetailDrawerComponent,
  ],
  templateUrl: './department-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './department-list.component.scss',
  ],
})
export class DepartmentListComponent extends LocalizedComponent {
  private readonly api = inject(DepartmentService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly reload = new Subject<void>();

  protected readonly departments = signal<DepartmentSummary[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly selectedDepartmentId = signal<string | null>(null);

  protected keyword = '';
  protected status: DepartmentStatus | '' = '';
  protected page = 1;
  protected readonly pageSize = 20;
  protected readonly statuses: readonly DepartmentStatus[] = ['ACTIVE', 'INACTIVE'];

  protected readonly selectedDepartment = computed(
    () => this.departments().find((d) => d.id === this.selectedDepartmentId()) ?? null,
  );
  protected readonly lastSyncAt = computed(() => {
    const times = this.departments()
      .map((d) => Date.parse(d.lastSyncAt))
      .filter((time) => !Number.isNaN(time));
    return times.length ? new Date(Math.max(...times)).toISOString() : null;
  });
  protected readonly syncText = computed(() => {
    const time = this.lastSyncAt();
    return time
      ? this.i18n.translate('department.syncedAt', {
          time: this.i18n.formatDate(time, 'DD/MM/YYYY HH:mm'),
        })
      : this.localization.department.syncUnavailable;
  });

  protected readonly paginationText = computed(() =>
    this.i18n.translate('department.deptPagination', {
      start: this.pageStart(),
      end: this.pageEnd(),
      total: this.totalCount(),
    }),
  );

  protected readonly pages = computed(() => {
    const total = this.totalPages();
    return Array.from({ length: total }, (_, i) => i + 1);
  });

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          const filter: DepartmentFilter = {
            keyword: this.keyword.trim() || undefined,
            status: this.status || undefined,
            page: this.page,
            pageSize: this.pageSize,
          };
          return this.api.list(filter).pipe(
            catchError(() => {
              this.failed.set(true);
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.departments.set(result.items);
        this.totalCount.set(result.totalCount);
        this.keepSelectionVisible(result.items);
      });
    this.refresh();
  }

  @HostListener('document:keydown.escape')
  protected closeDrawer(): void {
    this.selectedDepartmentId.set(null);
  }

  /** Arrow keys move the selection, except while the user types in or operates a form control. */
  @HostListener('document:keydown', ['$event'])
  protected onArrowKey(event: KeyboardEvent): void {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
    const target = event.target as HTMLElement | null;
    if (target?.closest('input, select, textarea, button, a, [contenteditable="true"]')) return;
    const items = this.departments();
    if (items.length === 0) return;
    const current = items.findIndex((d) => d.id === this.selectedDepartmentId());
    const step = event.key === 'ArrowDown' ? 1 : -1;
    const next = (current + step + items.length) % items.length;
    this.selectDepartment(items[next]);
    event.preventDefault();
  }

  protected refresh(resetPage = true): void {
    if (resetPage) this.page = 1;
    this.reload.next();
  }

  protected selectDepartment(department: DepartmentSummary): void {
    this.selectedDepartmentId.set(department.id);
  }

  protected goToPage(target: number): void {
    if (target < 1 || target > this.totalPages() || target === this.page) return;
    this.page = target;
    this.refresh(false);
  }

  protected totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount() / this.pageSize));
  }

  protected pageStart(): number {
    return this.totalCount() === 0 ? 0 : (this.page - 1) * this.pageSize + 1;
  }

  protected pageEnd(): number {
    return Math.min(this.page * this.pageSize, this.totalCount());
  }

  protected number(value: number | null | undefined): string {
    return displayNumber(
      value,
      this.i18n.currentLanguage(),
      this.localization.department.notAvailable,
    );
  }

  /** Keeps the query-selected department, otherwise drops a selection that left the current page. */
  private keepSelectionVisible(items: DepartmentSummary[]): void {
    const query = this.route.snapshot.queryParamMap;
    const requestedCode = query.get('department');
    const requestedId = query.get('departmentId');
    const requested =
      items.find((d) => requestedCode !== null && d.code === requestedCode) ??
      items.find((d) => requestedId !== null && d.id === requestedId);
    if (requested && this.selectedDepartmentId() === null && !this.queryHandled) {
      this.selectedDepartmentId.set(requested.id);
    }
    this.queryHandled = true;
    const selected = this.selectedDepartmentId();
    if (selected && !items.some((d) => d.id === selected)) this.selectedDepartmentId.set(null);
  }

  private queryHandled = false;
}
