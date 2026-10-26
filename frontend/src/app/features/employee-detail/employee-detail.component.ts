import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
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
import { AuthService } from '../../core/auth.service';
import { DepartmentService } from '../../core/department.service';
import { DepartmentMember } from '../../core/department.models';
import { EmployeeDetail, EmployeeStatus } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { displayNumber, initials } from '../../shared/ui/display';
import { ChangePasswordDialogComponent } from './change-password-dialog.component';
import { ChangePhoneDialogComponent } from './change-phone-dialog.component';

const CANDIDATE_PAGE_SIZE = 20;
/** The URL key that always means "my own record", so neither an id nor a code appears in the address. */
const SELF_KEY = 'me';
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

type Dialog = 'phone' | 'password';

@Component({
  selector: 'app-employee-detail',
  imports: [
    FormsModule,
    RouterLink,
    HeaderComponent,
    SidebarComponent,
    ChangePhoneDialogComponent,
    ChangePasswordDialogComponent,
  ],
  templateUrl: './employee-detail.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './employee-detail.component.scss',
  ],
})
export class EmployeeDetailComponent extends LocalizedComponent {
  private readonly employees = inject(EmployeeService);
  private readonly departments = inject(DepartmentService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  /** The URL key: `me`, an employee code, or (legacy links only) a GUID. Null shows the picker. */
  protected readonly employeeKey = signal<string | null>(null);
  protected readonly employee = signal<EmployeeDetail | null>(null);
  /**
   * True for the signed-in user's own record. Only decides whether the change buttons show; the backend endpoints
   * behind them (`/me/*`) can only ever act on the signed-in account.
   */
  protected readonly isSelf = computed(() => {
    const employee = this.employee();
    if (!employee) return false;
    if (this.employeeKey() === SELF_KEY) return true;
    const email = this.auth.currentUser()?.email;
    return !!email && email.toLowerCase() === employee.email.toLowerCase();
  });
  /** On `/employee-detail/me` there is no list to go back to, so the back link is hidden. */
  protected readonly isOwnAddress = computed(() => this.employeeKey() === SELF_KEY);
  protected readonly dialog = signal<Dialog | null>(null);
  /** Confirmation of the last completed change, shown after its popup closed. */
  protected readonly notice = signal<string | null>(null);
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
          const key = this.employeeKey();
          return (key ? this.loadDetail(key) : this.loadCandidates()).pipe(
            catchError((error: unknown) => {
              this.notFound.set(error instanceof HttpErrorResponse && error.status === 404);
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
        map(([params, query]) => params.get('code') ?? query.get('id')),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((key) => {
        this.employeeKey.set(key);
        this.employee.set(null);
        this.roster.set(null);
        this.dialog.set(null);
        this.notice.set(null);
        this.reload.next();
      });
  }

  protected refresh(): void {
    this.reload.next();
  }

  protected openDialog(dialog: Dialog): void {
    this.notice.set(null);
    this.dialog.set(dialog);
  }

  protected closeDialog(): void {
    this.dialog.set(null);
  }

  protected phoneChanged(): void {
    this.dialog.set(null);
    this.notice.set(this.localization.profile.phoneSaved);
    this.reload.next();
  }

  protected passwordChanged(): void {
    this.dialog.set(null);
    this.notice.set(this.localization.profile.passwordChanged);
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

  private fetch(key: string): Observable<EmployeeDetail> {
    if (key === SELF_KEY) return this.employees.me();
    if (!GUID_PATTERN.test(key)) return this.employees.byCode(key);
    // A link from before URLs used codes: resolve it, then swap the GUID in the address bar for the code.
    return this.employees.detail(key).pipe(
      tap(
        (employee) =>
          void this.router.navigate(['/employee-detail', employee.employeeCode], {
            replaceUrl: true,
          }),
      ),
    );
  }

  private loadDetail(key: string): Observable<void> {
    return this.fetch(key).pipe(
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
