import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import {
  StatusBadgeComponent,
  TablePaginationComponent,
  UiSearchInputComponent,
  UiSelectComponent,
} from './index';
import { UserAccountActionDialogComponent } from '../../features/user-accounts/user-account-action-dialog.component';
import { environment } from '../../../environments/environment';
import { UserAccountDetail } from '../../core/user-account.models';

describe('Shared UI Kit & UserAccountActionDialogComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        StatusBadgeComponent,
        TablePaginationComponent,
        UiSearchInputComponent,
        UiSelectComponent,
        UserAccountActionDialogComponent,
      ],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('renders StatusBadgeComponent tones cleanly', () => {
    const fixture = TestBed.createComponent(StatusBadgeComponent);
    fixture.componentRef.setInput('tone', 'warning');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.badge.tone-warning')).not.toBeNull();
  });

  it('emits value changes and clear events from UiSearchInputComponent', () => {
    const fixture = TestBed.createComponent(UiSearchInputComponent);
    fixture.componentRef.setInput('value', 'abc');
    fixture.detectChanges();
    let latest = 'abc';
    fixture.componentInstance.valueChange.subscribe((val) => (latest = val));
    (fixture.nativeElement.querySelector('.ui-search-clear') as HTMLButtonElement).click();
    expect(latest).toBe('');
  });

  it('opens UiSelectComponent, selects an option, and updates TablePaginationComponent page size', () => {
    const fixture = TestBed.createComponent(TablePaginationComponent);
    fixture.componentRef.setInput('page', 1);
    fixture.componentRef.setInput('pageSize', 20);
    fixture.componentRef.setInput('totalCount', 65);
    fixture.componentRef.setInput('pageSizeOptions', [20, 50]);
    fixture.detectChanges();

    let nextSize = 20;
    fixture.componentInstance.pageSizeChange.subscribe((s) => (nextSize = s));
    const trigger = fixture.nativeElement.querySelector('.ui-select-trigger') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    const opts = fixture.nativeElement.querySelectorAll('.ui-select-option');
    (opts[1] as HTMLElement).click();
    fixture.detectChanges();
    expect(nextSize).toBe(50);
  });

  it('confirms password reset in UserAccountActionDialogComponent and displays temporary password', () => {
    const account = {
      id: 'acc-1',
      username: 'user.one',
      employeeCode: 'NV01',
      fullName: 'User One',
      departmentCode: 'D01',
      departmentName: 'Department 1',
      jobPositionTitle: 'Teller',
      roleId: 'r1',
      roleName: 'Employee',
      status: 'ACTIVE',
      employeeId: 'e1',
      suggestedRoleId: null,
      createdAt: '2026-11-01T00:00:00Z',
      createdBy: 'System',
      passwordChangedAt: null,
      history: [],
    } as UserAccountDetail;

    const fixture = TestBed.createComponent(UserAccountActionDialogComponent);
    fixture.componentRef.setInput('account', account);
    fixture.componentRef.setInput('kind', 'reset');
    fixture.detectChanges();

    const confirmBtn = fixture.nativeElement.querySelector(
      '.modal-footer .button-primary',
    ) as HTMLButtonElement;
    confirmBtn.click();

    const req = http.expectOne(`${environment.apiUrl}/user-accounts/acc-1/reset-password`);
    expect(req.request.method).toBe('POST');
    req.flush({
      success: true,
      status: 200,
      data: { temporaryPassword: 'Tmp#ResetPassword99' },
    });
    fixture.detectChanges();

    const inputEl = fixture.nativeElement.querySelector(
      '#reset-temp-password',
    ) as HTMLInputElement | null;
    expect(inputEl?.value).toBe('Tmp#ResetPassword99');
  });
});
