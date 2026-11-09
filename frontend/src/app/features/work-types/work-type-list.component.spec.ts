import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/auth.service';
import { WorkTypeDetail } from '../../core/work-type.models';
import { WorkTypeListComponent } from './work-type-list.component';

const item = (over: Partial<WorkTypeDetail>): WorkTypeDetail => ({
  id: 'w1',
  code: 'WT-T-001',
  name: 'Mở tài khoản',
  description: null,
  productCode: null,
  status: 'ACTIVE',
  createdAt: '2026-10-12T09:30:00+07:00',
  kpis: [
    {
      kpiDefinitionId: 'k1',
      kpiCode: 'KPI-T01',
      kpiName: 'Tài khoản mới',
      isRequired: true,
      multiplier: 1,
      effectiveFrom: '2026-01-01',
      effectiveTo: null,
    },
  ],
  ...over,
});

describe('WorkTypeListComponent', () => {
  function setup() {
    TestBed.configureTestingModule({
      imports: [WorkTypeListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser: () => null,
            logout: () => undefined,
            isAuthenticated: () => true,
          },
        },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(WorkTypeListComponent);
    fixture.detectChanges();
    return { http, fixture };
  }

  function flushLookups(http: HttpTestingController) {
    http
      .match((r) => r.url === `${environment.apiUrl}/work-types/kpis`)
      .forEach((r) =>
        r.flush({
          success: true,
          data: [{ id: 'k1', code: 'KPI-T01', name: 'Tài khoản mới', unit: 'tài khoản' }],
        }),
      );
    http
      .match((r) => r.url === `${environment.apiUrl}/job-positions`)
      .forEach((r) =>
        r.flush({
          success: true,
          data: {
            items: [
              {
                id: 'p1',
                code: 'TELLER',
                title: 'Teller',
                description: null,
                employeeCount: 3,
                evaluationProfileId: null,
                evaluationProfileName: null,
                workTypeCount: 1,
                kpiItems: [],
                lastSyncAt: '2026-10-12T00:00:00+07:00',
                status: 'ACTIVE',
              },
            ],
            totalCount: 1,
            page: 1,
            pageSize: 100,
          },
        }),
      );
    http
      .match((r) => r.url === `${environment.apiUrl}/job-positions/p1/work-types`)
      .forEach((r) =>
        r.flush({
          success: true,
          data: [
            {
              id: 'w1',
              code: 'WT-T-001',
              name: 'Mở tài khoản',
              description: null,
              kpiCount: 1,
              kpis: [],
            },
          ],
        }),
      );
  }

  it('requests work types, positions, KPI options and renders rows with position and open works', () => {
    const { http, fixture } = setup();
    flushLookups(http);
    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/work-types`);
    req.flush({
      success: true,
      data: { items: [item({})], totalCount: 1, page: 1, pageSize: 100 },
    });
    http
      .match((r) => r.url === `${environment.apiUrl}/works`)
      .forEach((r) =>
        r.flush({ success: true, data: { items: [], totalCount: 42, page: 1, pageSize: 1 } }),
      );
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('WT-T-001');
    expect(text).toContain('Teller');
    expect(text).toContain('42');
  });

  it('shows N/A for the position when no job position maps the work type, never a guess from the code', () => {
    const { http, fixture } = setup();
    http
      .match((r) => r.url === `${environment.apiUrl}/job-positions`)
      .forEach((r) =>
        r.flush({ success: true, data: { items: [], totalCount: 0, page: 1, pageSize: 100 } }),
      );
    http
      .expectOne((r) => r.url === `${environment.apiUrl}/work-types`)
      .flush({
        success: true,
        data: { items: [item({ code: 'WT-S-001' })], totalCount: 1, page: 1, pageSize: 100 },
      });
    fixture.detectChanges();
    const badge = (fixture.nativeElement as HTMLElement).querySelector('.position-badge');
    expect(badge?.textContent?.trim()).toBe('N/A');
  });

  it('keeps the create action disabled because creating is not available yet', () => {
    const { http, fixture } = setup();
    flushLookups(http);
    http
      .expectOne((r) => r.url === `${environment.apiUrl}/work-types`)
      .flush({ success: true, data: { items: [], totalCount: 0, page: 1, pageSize: 100 } });
    fixture.detectChanges();
    const create = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.list-toolbar .button-primary',
    );
    expect(create?.disabled).toBeTrue();
  });

  it('opens the detail drawer with the KPI mappings when a row is clicked', () => {
    const { http, fixture } = setup();
    flushLookups(http);
    http
      .expectOne((r) => r.url === `${environment.apiUrl}/work-types`)
      .flush({
        success: true,
        data: { items: [item({})], totalCount: 1, page: 1, pageSize: 100 },
      });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLElement>('.work-type-row')?.click();
    fixture.detectChanges();
    const drawer = root.querySelector('app-work-type-detail-drawer');
    expect(drawer?.textContent).toContain('KPI-T01');
    root.querySelector<HTMLElement>('.close-button')?.click();
    fixture.detectChanges();
    expect(root.querySelector('app-work-type-detail-drawer')).toBeNull();
  });

  it('recalculates the page count when only the page size changes', () => {
    const { http, fixture } = setup();
    flushLookups(http);
    const items = Array.from({ length: 45 }, (_, i) =>
      item({ id: `w${i + 1}`, code: `WT-T-${i + 1}` }),
    );
    http
      .expectOne((r) => r.url === `${environment.apiUrl}/work-types`)
      .flush({
        success: true,
        data: { items, totalCount: 45, page: 1, pageSize: 100 },
      });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.page-button.is-current')?.textContent).toContain('1 / 3');

    const select = root.querySelector<HTMLSelectElement>('.page-size') as HTMLSelectElement;
    select.value = select.options[2].value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(root.querySelector('.page-button.is-current')?.textContent).toContain('1 / 1');
  });

  it('deactivates the selected work type after confirmation and reloads the list', () => {
    const { http, fixture } = setup();
    flushLookups(http);
    const listUrl = `${environment.apiUrl}/work-types`;
    http
      .expectOne((r) => r.url === listUrl)
      .flush({
        success: true,
        data: { items: [item({})], totalCount: 1, page: 1, pageSize: 100 },
      });
    http
      .match((r) => r.url === `${environment.apiUrl}/works`)
      .forEach((r) =>
        r.flush({ success: true, data: { items: [], totalCount: 7, page: 1, pageSize: 1 } }),
      );
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLElement>('.work-type-row')?.click();
    fixture.detectChanges();
    const usage = http.match((r) => r.url === `${environment.apiUrl}/works`);
    expect(usage.length).toBe(2);
    usage.forEach((r) =>
      r.flush({ success: true, data: { items: [], totalCount: 7, page: 1, pageSize: 1 } }),
    );
    fixture.detectChanges();
    expect(root.querySelector('.usage-grid')?.textContent).toContain('7');

    root.querySelector<HTMLElement>('.danger-button')?.click();
    fixture.detectChanges();
    const buttons = root.querySelectorAll<HTMLElement>('.drawer-footer .button-primary');
    buttons[buttons.length - 1].click();
    const patch = http.expectOne(`${listUrl}/w1/status`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ status: 'INACTIVE' });
    patch.flush({ success: true, status: 200, data: null });
    http.expectOne((r) => r.url === listUrl);
  });

  it('shows an error state when the request fails', () => {
    const { http, fixture } = setup();
    flushLookups(http);
    http
      .expectOne((r) => r.url === `${environment.apiUrl}/work-types`)
      .flush('err', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.state-error')).not.toBeNull();
  });
});
