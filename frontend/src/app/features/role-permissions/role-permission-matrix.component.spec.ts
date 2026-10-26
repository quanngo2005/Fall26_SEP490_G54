import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { RolePermissionMatrix } from '../../core/role-permission.models';
import { RolePermissionMatrixComponent } from './role-permission-matrix.component';

const matrix: RolePermissionMatrix = {
  roleId: 'r1',
  version: 3,
  modules: [
    {
      module: 'kpi',
      name: 'KPI & Mục tiêu',
      resources: [
        {
          resource: 'target',
          name: 'Mục tiêu nhân viên',
          cells: [
            { action: 'view', applicable: true, scope: 'Dept', locked: false },
            { action: 'create', applicable: true, scope: null, locked: true, lockReason: 'x' },
            { action: 'update', applicable: false, scope: null, locked: false },
          ],
        },
      ],
    },
  ],
};

describe('RolePermissionMatrixComponent', () => {
  let http: HttpTestingController;
  const rolesUrl = `${environment.apiUrl}/roles`;
  const matrixUrl = `${rolesUrl}/r1/permission-matrix`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RolePermissionMatrixComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function setup() {
    const fixture = TestBed.createComponent(RolePermissionMatrixComponent);
    fixture.detectChanges();
    http.expectOne(rolesUrl).flush({
      success: true,
      status: 200,
      data: [{ id: 'r1', code: 'DM', name: 'Trưởng phòng', isSystem: true }],
    });
    http.expectOne(matrixUrl).flush({ success: true, status: 200, data: matrix });
    fixture.detectChanges();
    return fixture;
  }

  function flushSidebarRequests() {
    http.match(() => true).forEach((req) => req.flush({ success: true, status: 200, data: [] }));
  }

  it('renders editable, locked and not-applicable cells', () => {
    const fixture = setup();
    flushSidebarRequests();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('select.scope-cell').length).toBe(1);
    expect(el.querySelectorAll('.scope-cell.is-locked').length).toBe(1);
    expect(el.querySelectorAll('.na').length).toBeGreaterThan(0);
  });

  it('tracks edits and saves only changed cells with version', () => {
    const fixture = setup();
    flushSidebarRequests();
    const component = fixture.componentInstance as unknown as {
      setScope: (r: string, c: unknown, v: string) => void;
      dirtyCount: () => number;
      save: () => void;
    };
    const cell = matrix.modules[0].resources[0].cells[0];
    component.setScope('target', cell, 'Branch');
    expect(component.dirtyCount()).toBe(1);
    component.setScope('target', cell, 'Dept');
    expect(component.dirtyCount()).toBe(0);

    component.setScope('target', cell, '');
    component.save();
    const req = http.expectOne((r) => r.method === 'PUT' && r.url === matrixUrl);
    expect(req.request.body).toEqual({
      version: 3,
      changes: [{ resource: 'target', action: 'view', scope: null }],
    });
    req.flush({ success: true, status: 200, data: { ...matrix, version: 4 } });
    expect(component.dirtyCount()).toBe(0);
  });
});
