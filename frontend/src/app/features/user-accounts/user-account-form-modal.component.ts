import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  computed,
  DestroyRef,
  HostListener,
  inject,
  input,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ApiResponse } from '../../core/api-response.model';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { RoleOption } from '../../core/role-permission.models';
import {
  EligibleEmployee,
  UserAccountDetail,
  UserAccountSummary,
} from '../../core/user-account.models';
import { UserAccountService } from '../../core/user-account.service';

const USERNAME_PATTERN = /^[a-z0-9._-]{3,50}$/;

/** SCR-ADM-01-M1 (create) and SCR-ADM-02-M1 (edit role) in one dialog. */
@Component({
  selector: 'app-user-account-form-modal',
  imports: [FormsModule],
  templateUrl: './user-account-form-modal.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/modal.scss',
    './user-account-form-modal.component.scss',
  ],
})
export class UserAccountFormModalComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(UserAccountService);
  private readonly destroyRef = inject(DestroyRef);

  /** Account being edited; `null` opens the create form. */
  readonly account = input<UserAccountDetail | null>(null);
  readonly roles = input<RoleOption[]>([]);
  readonly closed = output<void>();
  readonly saved = output<UserAccountDetail>();

  protected readonly eligible = signal<EligibleEmployee[]>([]);
  protected readonly loadingEmployees = signal(false);
  protected readonly employeesFailed = signal(false);
  protected readonly employeeId = signal('');
  protected readonly username = signal('');
  protected readonly roleId = signal('');
  protected readonly mustChangePassword = signal(true);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly serverError = signal<'usernameExists' | 'accountExists' | 'saveError' | null>(
    null,
  );
  protected readonly created = signal<{ username: string; temporaryPassword: string } | null>(null);
  protected readonly copied = signal(false);

  protected readonly isEdit = computed(() => this.account() !== null);
  protected readonly employee = computed(
    () => this.eligible().find((item) => item.id === this.employeeId()) ?? null,
  );
  protected readonly selectedRole = computed(
    () => this.roles().find((role) => role.id === this.roleId()) ?? null,
  );

  protected readonly usernameError = computed(() => {
    if (this.serverError() === 'usernameExists')
      return this.localization.userAccounts.usernameExists;
    if (!this.submitted() || this.isEdit()) return null;
    if (!this.username().trim()) return this.localization.userAccounts.required;
    return USERNAME_PATTERN.test(this.username().trim())
      ? null
      : this.localization.userAccounts.usernameFormat;
  });
  protected readonly employeeError = computed(() =>
    this.submitted() && !this.isEdit() && !this.employeeId()
      ? this.localization.userAccounts.required
      : null,
  );
  protected readonly roleError = computed(() =>
    this.submitted() && !this.roleId() ? this.localization.userAccounts.required : null,
  );

  /** Edit only: warns when the chosen role differs from the one suggested for the position. */
  protected readonly mismatch = computed(() => {
    const account = this.account();
    const role = this.selectedRole();
    if (!account?.suggestedRoleId || !role || role.id === account.suggestedRoleId) return null;
    return this.localization.userAccounts.mismatch
      .replace('{position}', account.jobPositionTitle ?? '—')
      .replace('{role}', role.name);
  });

  ngOnInit(): void {
    const account = this.account();
    if (account) {
      this.roleId.set(account.roleId);
      return;
    }
    this.loadEmployees();
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.close();
  }

  @HostListener('document:keydown.control.s', ['$event'])
  protected onSaveShortcut(event: Event): void {
    event.preventDefault();
    if (!this.created()) this.submit();
  }

  protected loadEmployees(): void {
    this.loadingEmployees.set(true);
    this.employeesFailed.set(false);
    this.api
      .eligibleEmployees()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (employees) => {
          this.eligible.set(employees);
          this.loadingEmployees.set(false);
        },
        error: () => {
          this.loadingEmployees.set(false);
          this.employeesFailed.set(true);
        },
      });
  }

  protected selectEmployee(id: string): void {
    this.employeeId.set(id);
    this.serverError.set(null);
    const employee = this.eligible().find((item) => item.id === id);
    this.username.set(employee?.suggestedUsername ?? '');
    this.roleId.set(employee?.suggestedRoleId ?? '');
  }

  protected setUsername(value: string): void {
    this.username.set(value);
    if (this.serverError() === 'usernameExists') this.serverError.set(null);
  }

  protected close(): void {
    if (this.saving() || this.created()) return;
    this.closed.emit();
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.serverError.set(null);
    const account = this.account();
    if (this.roleError() || (!account && (this.employeeError() || this.usernameError()))) return;
    this.saving.set(true);
    if (account) {
      this.api
        .update(account.id, { roleId: this.roleId() })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (updated) => {
            this.saving.set(false);
            this.saved.emit(updated);
            this.closed.emit();
          },
          error: (error: unknown) => this.fail(error),
        });
      return;
    }
    const username = this.username().trim();
    this.api
      .create({
        employeeId: this.employeeId(),
        username,
        roleId: this.roleId(),
        mustChangePassword: this.mustChangePassword(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.saving.set(false);
          this.created.set({ username, temporaryPassword: result.temporaryPassword });
          this.saved.emit(result.account);
        },
        error: (error: unknown) => this.fail(error),
      });
  }

  protected createdTitle(): string {
    return this.localization.userAccounts.createdTitle.replace(
      '{username}',
      this.created()?.username ?? '',
    );
  }

  protected copyPassword(): void {
    const password = this.created()?.temporaryPassword;
    if (!password) return;
    navigator.clipboard?.writeText(password).then(
      () => this.copied.set(true),
      () => this.copied.set(false),
    );
  }

  protected summaryLabel(item: UserAccountSummary | EligibleEmployee): string {
    return `${item.employeeCode} · ${item.fullName}`;
  }

  private fail(error: unknown): void {
    this.saving.set(false);
    const body = error instanceof HttpErrorResponse ? (error.error as ApiResponse<null>) : null;
    switch (body?.errorCode) {
      case 'USERNAME_EXISTS':
        this.serverError.set('usernameExists');
        break;
      case 'ACCOUNT_EXISTS':
        this.serverError.set('accountExists');
        break;
      default:
        this.serverError.set('saveError');
    }
  }
}
