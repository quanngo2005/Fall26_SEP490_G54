import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { RoleSummary } from '../../core/role-permission.models';
import { RoleListComponent } from './role-list.component';

const roles: RoleSummary[] = [
  { id: 'r1', code: 'TELLER', name: 'Giao dịch viên', isSystem: true, accountCount: 14, status: 'Active' },
  { id: 'r2', code: 'AUDIT_VIEW', name: 'Xem báo cáo kiểm toán', isSystem: false, accountCount: 0, status: 'Active' },
];

describe('RoleListComponent', () => {
  let http: HttpTestingController;
  const rolesUrl = `${environment.apiUrl}/roles`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RoleListComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function setup() {
    const fixture = TestBed.createComponent(RoleListComponent);
    fixture.detectChanges();
    http.expectOne(rolesUrl).flush({ success: true, status: 200, data: roles });
    fixture.detectChanges();
    return fixture;
  }

  it('renders roles and filters by type', () => {
    const fixture = setup();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('tbody tr').length).toBe(2);
    (fixture.componentInstance as any).typeFilter.set('custom');
    fixture.detectChanges();
    expect(el.querySelectorAll('tbody tr').length).toBe(1);
    expect(el.textContent).toContain('AUDIT_VIEW');
  });

  it('opens the drawer and shows granted permissions only', () => {
    const fixture = setup();
    const el: HTMLElement = fixture.nativeElement;
    (el.querySelector('tbody tr') as HTMLElement).click();
    fixture.detectChanges();
    http.expectOne(`${rolesUrl}/r1/permission-matrix`).flush({
      success: true,
      status: 200,
      data: {
        roleId: 'r1',
        version: 1,
        modules: [
          {
            module: 'work',
            name: 'Công việc',
            resources: [
              {
                resource: 'work',
                name: 'Công việc',
                cells: [
                  { action: 'view', applicable: true, scope: 'Own', locked: false },
                  { action: 'create', applicable: true, scope: 'Own', locked: false },
                  { action: 'approve', applicable: true, scope: null, locked: false },
                ],
              },
            ],
          },
        ],
      },
    });
    fixture.detectChanges();
    const rows = el.querySelectorAll('.drawer .perm-row:not(.perm-head)');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('Xem, Tạo');
  });
});
