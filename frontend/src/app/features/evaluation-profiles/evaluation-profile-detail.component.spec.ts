import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/auth.service';
import {
  EvaluationProfileDetail,
  EvaluationProfileItem,
  EvaluationProfileListItem,
} from '../../core/evaluation-profile.models';
import { EvaluationProfileDetailComponent } from './evaluation-profile-detail.component';

const url = `${environment.apiUrl}/evaluation-profiles`;

const leaf = (kpiDefinitionId: string, weightPercent: number): EvaluationProfileItem => ({
  id: `leaf-${kpiDefinitionId}`,
  parentItemId: 'g1',
  name: null,
  kpiDefinitionId,
  kpiCode: kpiDefinitionId.toUpperCase(),
  kpiName: `KPI ${kpiDefinitionId}`,
  weightPercent,
  displayOrder: 1,
  children: [],
});

const detail = (over: Partial<EvaluationProfileDetail>): EvaluationProfileDetail => ({
  id: 'p2',
  code: 'EP-TELLER',
  name: 'Teller profile',
  description: null,
  jobPositionId: 'j1',
  jobPositionCode: 'TELLER',
  jobPositionTitle: 'Teller',
  version: 2,
  effectiveFrom: '2026-10-01',
  effectiveTo: '2026-12-31',
  status: 'PENDING_APPROVAL',
  rejectReason: null,
  isInUse: false,
  totalWeightPercent: 100,
  updatedAt: '2026-10-23T07:00:00+00:00',
  items: [
    {
      id: 'g1',
      parentItemId: null,
      name: 'Sales',
      kpiDefinitionId: null,
      kpiCode: null,
      kpiName: null,
      weightPercent: 100,
      displayOrder: 1,
      children: [leaf('k1', 60), leaf('k2', 40)],
    },
  ],
  ...over,
});

const listItem = (over: Partial<EvaluationProfileListItem>): EvaluationProfileListItem => ({
  id: 'p1',
  code: 'EP-TELLER',
  name: 'Teller profile v1',
  jobPositionId: 'j1',
  jobPositionCode: 'TELLER',
  jobPositionTitle: 'Teller',
  version: 1,
  effectiveFrom: '2026-07-01',
  effectiveTo: '2026-09-30',
  status: 'INACTIVE',
  itemCount: 2,
  totalWeightPercent: 100,
  updatedAt: '2026-07-01T07:00:00+00:00',
  ...over,
});

/** The protected members the specs drive directly. */
interface DetailInternals {
  profile(): EvaluationProfileDetail | null;
  previous(): EvaluationProfileDetail | null;
  loading(): boolean;
  failed(): boolean;
  notFound(): boolean;
  acting(): boolean;
  actionError(): string | null;
  rejecting(): boolean;
  canDecide(): boolean;
  criteria(): { kpi: string; weight: number; previous: number | null }[];
  criteriaTotal(): number;
  reason: string;
  approve(): void;
  openReject(): void;
  reject(): void;
  reasonValid(): boolean;
}

/** The sidebar loads its own configuration once a user is signed in; that request is not what these specs check. */
function verifyNoOtherRequests(http: HttpTestingController): void {
  http.match((r) => r.url === `${environment.apiUrl}/sidebar-configurations/current`);
  http.verify();
}

describe('EvaluationProfileDetailComponent', () => {
  function setup(permissions: string[], id = 'p2') {
    TestBed.configureTestingModule({
      imports: [EvaluationProfileDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(convertToParamMap({ id })),
            snapshot: { paramMap: convertToParamMap({ id }) },
          },
        },
        {
          provide: AuthService,
          useValue: {
            currentUser: () => ({ permissions }),
            logout: () => undefined,
            isAuthenticated: () => true,
          },
        },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(EvaluationProfileDetailComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance as unknown as DetailInternals;
    return { http, fixture, component, element: fixture.nativeElement as HTMLElement };
  }

  /** Answers the detail request, then the lookup of earlier versions (none unless `earlier` is given). */
  function load(
    http: HttpTestingController,
    profile: EvaluationProfileDetail | 'not-found' | 'error',
    earlier: EvaluationProfileListItem[] = [],
    earlierDetail?: EvaluationProfileDetail,
  ) {
    const req = http.expectOne(`${url}/${encodeURIComponent('p2')}`);
    if (profile === 'not-found') {
      req.flush({}, { status: 404, statusText: 'Not Found' });
      return;
    }
    if (profile === 'error') {
      req.flush({}, { status: 500, statusText: 'Server Error' });
      return;
    }
    req.flush({ success: true, data: profile });
    const list = http.expectOne((r) => r.url === url);
    expect(list.request.params.get('keyword')).toBe(profile.code);
    expect(list.request.params.get('jobPositionId')).toBe(profile.jobPositionId);
    list.flush({
      success: true,
      data: { items: earlier, totalCount: earlier.length, page: 1, pageSize: 100 },
    });
    if (earlierDetail) {
      http.expectOne(`${url}/${earlierDetail.id}`).flush({ success: true, data: earlierDetail });
    }
  }

  it('shows the profile and the weight of each KPI in the whole profile', () => {
    const { http, fixture, component, element } = setup([]);
    load(http, detail({}));
    fixture.detectChanges();

    expect(element.textContent).toContain('Teller profile');
    expect(component.criteria().map((row) => [row.kpi, row.weight])).toEqual([
      ['KPI k1', 60],
      ['KPI k2', 40],
    ]);
    expect(component.criteriaTotal()).toBe(100);
    verifyNoOtherRequests(http);
  });

  it('compares the weights with the latest earlier version of the same profile', () => {
    const { http, component } = setup([]);
    const before = detail({
      id: 'p1',
      version: 1,
      items: [
        {
          ...detail({}).items[0],
          children: [leaf('k1', 50), leaf('k3', 50)],
        },
      ],
    });
    load(
      http,
      detail({}),
      [listItem({ id: 'p1', version: 1 }), listItem({ id: 'p9', code: 'OTHER', version: 1 })],
      before,
    );

    expect(component.previous()?.id).toBe('p1');
    expect(component.criteria().map((row) => [row.kpi, row.previous])).toEqual([
      ['KPI k1', 50],
      ['KPI k2', null],
    ]);
    verifyNoOtherRequests(http);
  });

  it('shows the not found state for an unknown profile and the failed state for any other error', () => {
    const notFound = setup([]);
    load(notFound.http, 'not-found');
    expect(notFound.component.notFound()).toBeTrue();
    expect(notFound.component.failed()).toBeFalse();
    TestBed.resetTestingModule();

    const failed = setup([]);
    load(failed.http, 'error');
    expect(failed.component.failed()).toBeTrue();
    expect(failed.component.notFound()).toBeFalse();
    expect(failed.component.loading()).toBeFalse();
  });

  it('lets only a holder of the approve permission decide a pending profile', () => {
    const allowed = setup(['evaluation-profile:approve']);
    load(allowed.http, detail({}));
    expect(allowed.component.canDecide()).toBeTrue();
    TestBed.resetTestingModule();

    const noPermission = setup(['kpi:manage']);
    load(noPermission.http, detail({}));
    expect(noPermission.component.canDecide()).toBeFalse();
    TestBed.resetTestingModule();

    const notPending = setup(['evaluation-profile:approve']);
    load(notPending.http, detail({ status: 'ACTIVE' }));
    expect(notPending.component.canDecide()).toBeFalse();
  });

  it('approves the profile and reloads it', () => {
    const { http, component } = setup(['evaluation-profile:approve']);
    load(http, detail({}));

    component.approve();
    const approve = http.expectOne(`${url}/p2/approve`);
    expect(approve.request.method).toBe('POST');
    expect(component.acting()).toBeTrue();
    approve.flush({ success: true });

    expect(component.acting()).toBeFalse();
    load(http, detail({ status: 'ACTIVE' }));
    expect(component.profile()?.status).toBe('ACTIVE');
    verifyNoOtherRequests(http);
  });

  it('does not approve without the permission', () => {
    const { http, component } = setup([]);
    load(http, detail({}));

    component.approve();

    http.expectNone(`${url}/p2/approve`);
    verifyNoOtherRequests(http);
  });

  it('needs a reason of 1 to 500 characters to reject', () => {
    const { http, component } = setup(['evaluation-profile:approve']);
    load(http, detail({}));
    component.openReject();

    component.reason = '   ';
    expect(component.reasonValid()).toBeFalse();
    component.reject();
    http.expectNone(`${url}/p2/reject`);

    component.reason = 'x'.repeat(501);
    expect(component.reasonValid()).toBeFalse();
    component.reason = 'x'.repeat(500);
    expect(component.reasonValid()).toBeTrue();
  });

  it('rejects with the trimmed reason and shows the message of a failed decision', () => {
    const { http, component } = setup(['evaluation-profile:approve']);
    load(http, detail({}));
    component.openReject();
    component.reason = '  Weights are wrong  ';

    component.reject();
    const reject = http.expectOne(`${url}/p2/reject`);
    expect(reject.request.body).toEqual({ reason: 'Weights are wrong' });
    reject.flush(
      { success: false, message: 'Profile is no longer pending.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.actionError()).toBe('Profile is no longer pending.');
    expect(component.acting()).toBeFalse();
    expect(component.rejecting()).toBeTrue();
  });
});
