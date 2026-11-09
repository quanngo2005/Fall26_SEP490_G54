import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/auth.service';
import { EvaluationProfileListItem } from '../../core/evaluation-profile.models';
import { EvaluationProfileListComponent } from './evaluation-profile-list.component';

const profile = (over: Partial<EvaluationProfileListItem>): EvaluationProfileListItem => ({
  id: 'p1',
  code: 'EP-TELLER',
  name: 'Teller profile',
  jobPositionId: 'j1',
  jobPositionCode: 'TELLER',
  jobPositionTitle: 'Teller',
  version: 1,
  effectiveFrom: '2026-10-01',
  effectiveTo: '2026-12-31',
  status: 'ACTIVE',
  itemCount: 3,
  totalWeightPercent: 100,
  updatedAt: '2026-10-23T07:00:00+00:00',
  ...over,
});

/** The protected members the specs drive directly. */
interface ListInternals {
  keyword: string;
  status: EvaluationProfileListItem['status'] | '';
  applyFilter(): void;
  clearFilter(): void;
  periodLabel(item: EvaluationProfileListItem): string;
  isLocked(item: EvaluationProfileListItem): boolean;
  isViewOnly(item: EvaluationProfileListItem): boolean;
  filteredProfiles(): EvaluationProfileListItem[];
  allProfiles(): EvaluationProfileListItem[];
  failed(): boolean;
  loading(): boolean;
}
describe('EvaluationProfileListComponent', () => {
  function setup(items: EvaluationProfileListItem[] | 'error') {
    TestBed.configureTestingModule({
      imports: [EvaluationProfileListComponent],
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
    const fixture = TestBed.createComponent(EvaluationProfileListComponent);
    fixture.detectChanges();
    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/evaluation-profiles`);
    if (items === 'error') req.flush({}, { status: 500, statusText: 'Server Error' });
    else
      req.flush({
        success: true,
        data: { items, totalCount: items.length, page: 1, pageSize: 100 },
      });
    fixture.detectChanges();
    const component = fixture.componentInstance as unknown as ListInternals;
    return { http, fixture, component, element: fixture.nativeElement as HTMLElement };
  }

  it('loads the profiles from the API and renders their code and job position', () => {
    const { http, element } = setup([profile({})]);

    expect(element.textContent).toContain('Teller profile');
    expect(element.textContent).toContain('Teller');
    http.verify();
  });

  it('labels a whole calendar quarter as Qn/yyyy and any other range as dates', () => {
    const { component } = setup([]);

    expect(component.periodLabel(profile({}))).toBe('Q4/2026');
    expect(component.periodLabel(profile({ effectiveFrom: '2026-10-15' }))).toContain('15/10/2026');
  });

  it('filters by status and keyword in the browser and resets to the first page', () => {
    const { component } = setup([
      profile({ id: 'a', code: 'EP-A', name: 'Alpha', status: 'ACTIVE' }),
      profile({ id: 'b', code: 'EP-B', name: 'Beta', status: 'DRAFT' }),
    ]);

    component.status = 'DRAFT';
    component.applyFilter();
    expect(component.filteredProfiles().map((p) => p.id)).toEqual(['b']);

    component.clearFilter();
    component.keyword = 'alp';
    component.applyFilter();
    expect(component.filteredProfiles().map((p) => p.id)).toEqual(['a']);
  });

  it('treats an archived profile or one whose period has ended as locked', () => {
    const { component } = setup([]);

    expect(component.isLocked(profile({ status: 'ARCHIVED', effectiveTo: null }))).toBeTrue();
    expect(component.isLocked(profile({ effectiveTo: '2020-01-01' }))).toBeTrue();
    expect(component.isLocked(profile({ effectiveTo: null }))).toBeFalse();
  });

  it('only lets an approved or locked profile be viewed, any other profile can be edited', () => {
    const { component } = setup([]);

    expect(component.isViewOnly(profile({ status: 'ACTIVE', effectiveTo: null }))).toBeTrue();
    expect(component.isViewOnly(profile({ status: 'INACTIVE', effectiveTo: null }))).toBeTrue();
    expect(component.isViewOnly(profile({ status: 'ARCHIVED', effectiveTo: null }))).toBeTrue();
    expect(
      component.isViewOnly(profile({ status: 'DRAFT', effectiveTo: '2020-01-01' })),
    ).toBeTrue();
    expect(component.isViewOnly(profile({ status: 'DRAFT', effectiveTo: null }))).toBeFalse();
    expect(
      component.isViewOnly(profile({ status: 'PENDING_APPROVAL', effectiveTo: null })),
    ).toBeFalse();
    expect(component.isViewOnly(profile({ status: 'REJECTED', effectiveTo: null }))).toBeFalse();
  });

  it('shows the failed state and no rows when the API call fails', () => {
    const { component } = setup('error');

    expect(component.failed()).toBeTrue();
    expect(component.loading()).toBeFalse();
    expect(component.allProfiles()).toEqual([]);
  });
});
