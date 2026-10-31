import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { UserAccountDetail, UserAccountSummary } from '../../core/user-account.models';
import { UserAccountListComponent } from './user-account-list.component';

const summary = (id: string, username: string, status: UserAccountSummary['status']) => ({
  id,
  username,
  employeeCode: 'NV0012',
  fullName: 'Nguyễn Văn A',
  departmentCode: 'PGD-01',
  jobPositionTitle: 'Giao dịch viên',
  roleId: 'r1',
  roleName: 'Teller',
  status,
  lastLoginAt: '2026-10-24T10:42:00Z',
});

const detail: UserAccountDetail = {
  ...summary('a1', 'nva.teller', 'ACTIVE'),
  employeeId: 'e1',
  departmentName: 'Phòng Giao dịch 1',
  suggestedRoleId: 'r1',
  createdAt: '2022-03-15T00:00:00Z',
  createdBy: 'sa.admin',
  passwordChangedAt: null,
  history: [{ occurredAt: '2026-09-02T10:11:00Z', event: 'Reset mật khẩu', actor: 'sa.admin' }],
};

describe('UserAccountListComponent', () => {
  let http: HttpTestingController;
  const base = environment.apiUrl;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UserAccountListComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function flush(path: string, data: unknown): void {
    http.expectOne((r) => r.url === `${base}${path}`).flush({ success: true, status: 200, data });
  }

  function setup() {
    TestBed.inject(I18nService).setLanguage('vi');
    const fixture = TestBed.createComponent(UserAccountListComponent);
    fixture.detectChanges();
    flush('/roles', [{ id: 'r1', code: 'TELLER', name: 'Teller', isSystem: true }]);
    flush('/departments', { items: [], totalCount: 0, page: 1, pageSize: 100 });
    flush('/user-accounts/summary', {
      activeCount: 41,
      lockedCount: 3,
      employeesWithoutAccountCount: 4,
    });
    flush('/user-accounts', {
      items: [
        summary('a1', 'nva.teller', 'ACTIVE'),
        summary('a2', 'ntk.teller', 'EMPLOYEE_TERMINATED'),
      ],
      totalCount: 2,
      page: 1,
      pageSize: 20,
    });
    fixture.detectChanges();
    return fixture;
  }

  it('renders rows and hides edit for terminated employees', () => {
    const el: HTMLElement = setup().nativeElement;
    const rows = el.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('nva.teller');
    expect(rows[0].querySelectorAll('button').length).toBe(3);
    expect(rows[1].querySelectorAll('button').length).toBe(1);
    expect(el.querySelector('.quick-card strong')?.textContent).toContain('41');
  });

  it('opens the detail drawer', () => {
    const fixture = setup();
    const el: HTMLElement = fixture.nativeElement;
    (el.querySelector('tbody tr .link-button') as HTMLButtonElement).click();
    flush('/user-accounts/a1', detail);
    fixture.detectChanges();
    expect(el.querySelector('.drawer h2')?.textContent).toContain('nva.teller');
    expect(el.querySelector('.history-card')?.textContent).toContain('Reset mật khẩu');
  });

  it('shows an error state with retry when the list fails', () => {
    TestBed.inject(I18nService).setLanguage('vi');
    const fixture = TestBed.createComponent(UserAccountListComponent);
    fixture.detectChanges();
    flush('/roles', []);
    flush('/departments', { items: [], totalCount: 0, page: 1, pageSize: 100 });
    flush('/user-accounts/summary', {
      activeCount: 0,
      lockedCount: 0,
      employeesWithoutAccountCount: 0,
    });
    http
      .expectOne((r) => r.url === `${base}/user-accounts`)
      .flush({ success: false }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).not.toBeNull();
  });
});
