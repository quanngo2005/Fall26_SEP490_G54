import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  catchError,
  combineLatest,
  distinctUntilChanged,
  EMPTY,
  finalize,
  map,
  Observable,
  of,
  Subject,
  switchMap,
  tap,
} from 'rxjs';
import { apiErrorStatus } from '../../core/api-utils';
import { DepartmentService } from '../../core/department.service';
import { DepartmentMember } from '../../core/department.models';
import { EmployeeDetail, EmployeeStatus } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { displayNumber, initials } from '../../shared/ui/display';

const CANDIDATE_PAGE_SIZE = 20;

@Component({
  selector: 'app-employee-detail',
  imports: [FormsModule, RouterLink, HeaderComponent, SidebarComponent],
  templateUrl: './employee-detail.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './employee-detail.component.scss',
  ],
})
export class EmployeeDetailComponent extends LocalizedComponent {
  private readonly employees = inject(EmployeeService);
  private readonly departments = inject(DepartmentService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  protected readonly employeeId = signal<string | null>(null);
  protected readonly employee = signal<EmployeeDetail | null>(null);
  /** Roster row of the employee; null when the department roster has no data for them. */
  protected readonly roster = signal<DepartmentMember | null>(null);
  protected readonly candidates = signal<EmployeeDetail[]>([]);
  protected readonly candidateTotal = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly notFound = signal(false);
  protected keyword = '';

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          this.notFound.set(false);
          const id = this.employeeId();
          return (id ? this.loadDetail(id) : this.loadCandidates()).pipe(
            catchError((error: unknown) => {
              this.notFound.set(apiErrorStatus(error) === 404);
              this.failed.set(true);
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    combineLatest([this.route.paramMap, this.route.queryParamMap])
      .pipe(
        map(([params, query]) => params.get('id') ?? query.get('id')),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((id) => {
        this.employeeId.set(id);
        this.employee.set(null);
        this.roster.set(null);
        this.reload.next();
      });
  }

  protected refresh(): void {
    this.reload.next();
  }

  protected search(): void {
    this.reload.next();
  }

  protected display(value: number | null | undefined): string {
    return displayNumber(
      value,
      this.i18n.currentLanguage(),
      this.localization.employee.notAvailable,
    );
  }

  protected text(value: string | null | undefined): string {
    return value?.trim() ? value : this.localization.employee.notAvailable;
  }

  protected initials(name: string): string {
    return initials(name);
  }

  protected statusLabel(status: EmployeeStatus | null): string {
    const text = this.localization.employee;
    switch (status) {
      case 'ACTIVE':
        return text.statusActive;
      case 'INACTIVE':
        return text.statusInactive;
      case 'ON_LEAVE':
        return text.statusOnLeave;
      case 'TERMINATED':
        return text.statusTerminated;
      default:
        return text.notAvailable;
    }
  }

  protected isActive(status: EmployeeStatus | null): boolean {
    return status === 'ACTIVE';
  }

  private loadDetail(id: string): Observable<void> {
    return this.employees.detail(id).pipe(
      tap((employee) => this.employee.set(employee)),
      switchMap((employee) =>
        this.departments
          .members(employee.departmentId, { keyword: employee.employeeCode, pageSize: 10 })
          .pipe(
            map((page) => page.items.find((member) => member.id === employee.id) ?? null),
            // The roster is secondary data scoped by the department permission; N/A when unavailable.
            catchError(() => of(null)),
          ),
      ),
      tap((member) => this.roster.set(member)),
      map(() => undefined),
    );
  }

  private loadCandidates(): Observable<void> {
    return this.employees
      .list({ keyword: this.keyword.trim(), pageSize: CANDIDATE_PAGE_SIZE })
      .pipe(
        tap((page) => {
          this.candidates.set(page.items);
          this.candidateTotal.set(page.totalCount);
        }),
        map(() => undefined),
      );
  }
}

export default EmployeeDetailComponent;
