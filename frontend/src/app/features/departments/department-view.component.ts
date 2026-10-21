import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, EMPTY, finalize, forkJoin, Subject, switchMap } from 'rxjs';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentService } from '../../core/department.service';
import {
  DepartmentMemberFilter,
  DepartmentMembers,
  DepartmentOverview,
  DepartmentSummary,
  MemberStatus,
} from '../../core/department.models';

@Component({
  selector: 'app-department-view',
  imports: [FormsModule, RouterLink, HeaderComponent, SidebarComponent, LayoutIconComponent],
  templateUrl: './department-view.component.html',
  styleUrls: ['../../shared/ui/ui-controls.scss', './department-view.component.scss'],
})
export class DepartmentViewComponent extends LocalizedComponent {
  private readonly api = inject(DepartmentService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly reload = new Subject<void>();
  protected readonly departments = signal<DepartmentSummary[]>([]);
  protected readonly overview = signal<DepartmentOverview | null>(null);
  protected readonly members = signal<DepartmentMembers>({
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 20,
  });
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected departmentId = '';
  protected periodId = '';
  protected keyword = '';
  protected status: MemberStatus | '' = '';
  protected page = 1;
  protected readonly statuses = ['ACTIVE', 'INACTIVE', 'ON_LEAVE', 'TERMINATED'] as const;

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          const filter: DepartmentMemberFilter = {
            keyword: this.keyword,
            status: this.status || undefined,
            periodId: this.periodId || undefined,
            page: this.page,
          };
          return forkJoin({
            overview: this.api.overview(this.departmentId, this.periodId || undefined),
            members: this.api.members(this.departmentId, filter),
          }).pipe(
            catchError(() => {
              this.failed.set(true);
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.overview.set(result.overview);
          this.members.set(result.members);
        },
        error: () => this.failed.set(true),
      });
    this.loadDepartments();
  }

  protected loadDepartments(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api
      .list({ pageSize: 100 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.departments.set(list.items);
          // Links carry the department code; `departmentId` is only read for links that predate that.
          const query = this.route.snapshot.queryParamMap;
          const requestedCode = query.get('department');
          const legacyId = query.get('departmentId');
          this.departmentId =
            list.items.find((d) => d.code === requestedCode)?.id ??
            list.items.find((d) => d.id === legacyId)?.id ??
            list.items.find((d) => d.levelType === 'DEPARTMENT')?.id ??
            list.items[0]?.id ??
            '';
          if (this.departmentId) this.refresh();
          else this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected refresh(resetPage = true): void {
    if (resetPage) this.page = 1;
    this.reload.next();
  }

  protected changeDepartment(): void {
    this.periodId = '';
    this.refresh();
  }

  protected nextPage(delta: number): void {
    this.page += delta;
    this.refresh(false);
  }

  protected display(value: number | null | undefined): string {
    return value == null
      ? this.localization.department.notAvailable
      : new Intl.NumberFormat(this.i18n.currentLanguage(), { maximumFractionDigits: 2 }).format(
          value,
        );
  }

  protected statusLabel(status: string): string {
    const text = this.localization.department;
    switch (status) {
      case 'ACTIVE':
        return text.active;
      case 'INACTIVE':
        return text.inactive;
      case 'ARCHIVED':
        return text.archived;
      case 'ON_LEAVE':
        return text.leave;
      case 'TERMINATED':
        return text.terminated;
      default:
        return text.notAvailable;
    }
  }

  protected initials(name: string): string {
    return name
      .split(/\s+/)
      .filter(Boolean)
      .slice(-2)
      .map((part) => part[0])
      .join('');
  }

  protected totalPages(): number {
    const { totalCount, pageSize } = this.members();
    return Math.max(1, Math.ceil(totalCount / (pageSize || 1)));
  }

  protected width(score: number | null): number {
    return Math.min(100, Math.max(0, score ?? 0));
  }
}
