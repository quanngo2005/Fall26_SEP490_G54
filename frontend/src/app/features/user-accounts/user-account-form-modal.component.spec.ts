import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { UserAccountDetail } from '../../core/user-account.models';
import { UserAccountFormModalComponent } from './user-account-form-modal.component';

const roles = [
  { id: 'r1', code: 'TELLER', name: 'Teller', isSystem: true },
  { id: 'r2', code: 'SALES', name: 'Sales Officer', isSystem: true },
];
const url = `${environment.apiUrl}/user-accounts`;

describe('UserAccountFormModalComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UserAccountFormModalComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  function create(account: UserAccountDetail | null) {
    const fixture = TestBed.createComponent(UserAccountFormModalComponent);
    fixture.componentRef.setInput('account', account);
    fixture.componentRef.setInput('roles', roles);
    fixture.detectChanges();
    return fixture;
  }

  async function set(fixture: ComponentFixture<unknown>, selector: string, value: string) {
    const el = (fixture.nativeElement as HTMLElement).querySelector(selector) as
      HTMLInputElement | HTMLSelectElement;
    el.value = value;
    el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('prefills username and role from the chosen employee and shows the temporary password once created', async () => {
    const fixture = create(null);
    http.expectOne(`${url}/eligible-employees`).flush({
      success: true,
      status: 200,
      data: [
        {
          id: 'e1',
          employeeCode: 'NV0031',
          fullName: 'Vũ Thị G',
          departmentCode: 'PGD-02',
          departmentName: 'PGD-02',
          jobPositionTitle: 'Chuyên viên bán hàng',
          email: 'vtg@bank.vn',
          source: 'Import batch #123',
          suggestedRoleId: 'r2',
          suggestedUsername: 'vtg.so',
        },
      ],
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await set(fixture, '#account-employee', 'e1');
    const el: HTMLElement = fixture.nativeElement;
    expect((el.querySelector('#account-username') as HTMLInputElement).value).toBe('vtg.so');
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const req = http.expectOne(url);
    expect(req.request.body).toEqual({
      employeeId: 'e1',
      username: 'vtg.so',
      roleId: 'r2',
      mustChangePassword: true,
    });
    req.flush({ success: true, status: 201, data: { account: {}, temporaryPassword: 'Tmp#1' } });
    fixture.detectChanges();
    expect((el.querySelector('#temp-password') as HTMLInputElement).value).toBe('Tmp#1');

    let closed = false;
    fixture.componentInstance.closed.subscribe(() => (closed = true));
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    (el.querySelector('.modal-backdrop') as HTMLElement).click();
    expect(closed).toBe(false);
  });

  it('shows the username-exists error returned by the API', async () => {
    const fixture = create(null);
    http.expectOne(`${url}/eligible-employees`).flush({
      success: true,
      status: 200,
      data: [
        {
          id: 'e1',
          employeeCode: 'NV0031',
          fullName: 'Vũ Thị G',
          departmentCode: 'PGD-02',
          departmentName: 'PGD-02',
          jobPositionTitle: null,
          email: 'vtg@bank.vn',
          source: 'x',
          suggestedRoleId: 'r1',
          suggestedUsername: 'vtg.so',
        },
      ],
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await set(fixture, '#account-employee', 'e1');
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );
    http
      .expectOne(url)
      .flush(
        { success: false, status: 409, errorCode: 'USERNAME_EXISTS', data: null },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.field-error')?.textContent,
    ).toContain('Username đã tồn tại');
  });

  it('warns when the edited role differs from the position suggestion and PUTs the new role', async () => {
    const account = {
      id: 'a1',
      username: 'nva.teller',
      employeeCode: 'NV0012',
      fullName: 'Nguyễn Văn A',
      jobPositionTitle: 'Giao dịch viên',
      roleId: 'r1',
      suggestedRoleId: 'r1',
    } as UserAccountDetail;
    const fixture = create(account);
    await fixture.whenStable();
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.callout.is-warning')).toBeNull();
    await set(fixture, '#account-role', 'r2');
    expect(el.querySelector('.callout.is-warning')?.textContent).toContain('Sales Officer');
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const req = http.expectOne(`${url}/a1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ roleId: 'r2' });
    req.flush({ success: true, status: 200, data: { ...account, roleId: 'r2' } });
  });
});
