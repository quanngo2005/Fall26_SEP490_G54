import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AppealListItem } from '../../core/appeal.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { DepartmentAppealsComponent } from './department-appeals.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const url = `${environment.apiUrl}/appeals`;
const item = (over: Partial<AppealListItem> = {}): AppealListItem => ({
  id: '9a8b7c6d-1111-2222-3333-444455556666',
  resultSubmissionId: null,
  workId: null,
  workCode: null,
  workTitle: null,
  submissionNo: null,
  employeeId: 'e2',
  employeeCode: 'NV002',
  employeeName: 'Lê Thị Mai',
  status: 'OPEN',
  filedAt: '2026-10-24T10:00:00+00:00',
  decidedAt: null,
  updatedAt: '2026-10-24T10:00:00+00:00',
  type: 'SCORE',
  performanceEvaluationId: 'pe1',
  periodCode: '2026-Q2',
  jobPositionTitle: 'Giao dịch viên',
  resolvedByEmployeeName: null,
  note: null,
  ...over,
});
const page = (items: AppealListItem[], totalCount = items.length) => ({
  success: true,
  status: 200,
  data: { items, totalCount, page: 1, pageSize: 20 },
});

describe('DepartmentAppealsComponent', () => {
  let http: HttpTestingController;

  const open = () => {
    const fixture = TestBed.createComponent(DepartmentAppealsComponent);
    fixture.detectChanges();
    return fixture;
  };
  const flushLookups = () => {
    http.expectOne(`${environment.apiUrl}/evaluations/periods`).flush({
      success: true,
      status: 200,
      data: [{ id: 'p1', code: '2026-Q2', name: 'Q2', status: 'OPEN' }],
    });
    http.expectOne(`${environment.apiUrl}/employees/job-positions`).flush({
      success: true,
      status: 200,
      data: [{ id: 'j1', code: 'TLR', title: 'Giao dịch viên' }],
    });
  };
  const flushList = (list: AppealListItem[], needsAction = list.length, resolved = 0) => {
    const requests = http.match((r) => r.url === url);
    expect(requests.length).toBe(3);
    for (const request of requests) {
      expect(request.request.params.get('scope')).toBe('TEAM');
      if (request.request.params.get('pageSize') === '1') {
        const resolvedCount =
          request.request.params.get('status') === 'ACCEPTED,REJECTED,WITHDRAWN';
        request.flush(page([], resolvedCount ? resolved : needsAction));
      } else {
        request.flush(page(list));
      }
    }
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DepartmentAppealsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    })
      .overrideComponent(DepartmentAppealsComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('lists the other employees appeals of the caller scope and shows the real rows', () => {
    const fixture = open();
    flushLookups();
    flushList([item()], 4, 9);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const rows = root.querySelectorAll('tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('9A8B7C6D');
    expect(rows[0].textContent).toContain('Lê Thị Mai');
    expect(rows[0].textContent).toContain('Giao dịch viên');
    expect(rows[0].textContent).toContain('2026-Q2');
    expect(root.textContent).not.toContain('APL-0418');
    const counts = Array.from(root.querySelectorAll('.tab-count')).map((e) =>
      e.textContent?.trim(),
    );
    expect(counts).toEqual(['4', '9']);
  });

  it('shows N/A for the due date because the API has no processing deadline', () => {
    const fixture = open();
    flushLookups();
    flushList([item()]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('tbody td.col-deadline').textContent.trim()).toBe(
      'N/A',
    );
  });

  it('sends the period, position and type filters to the API', () => {
    const fixture = open();
    flushLookups();
    flushList([]);
    fixture.detectChanges();
    const selects = fixture.nativeElement.querySelectorAll('select');
    selects[0].value = 'SCORE';
    selects[0].dispatchEvent(new Event('change'));
    const first = http.match((r) => r.url === url);
    expect(
      first.find((r) => r.request.params.get('pageSize') !== '1')?.request.params.get('type'),
    ).toBe('SCORE');
    for (const request of first) request.flush(page([]));
    fixture.detectChanges();

    selects[1].value = 'p1';
    selects[1].dispatchEvent(new Event('change'));
    selects[2].value = 'j1';
    selects[2].dispatchEvent(new Event('change'));
    // Each change cancels the requests of the one before it; only the last set is still live.
    const later = http.match((r) => r.url === url).filter((r) => !r.cancelled);
    const main = later.find((r) => r.request.params.get('pageSize') !== '1');
    expect(main?.request.params.get('periodId')).toBe('p1');
    expect(main?.request.params.get('jobPositionId')).toBe('j1');
    expect(main?.request.params.get('type')).toBe('SCORE');
    for (const request of later) request.flush(page([]));
  });

  it('keeps working when the period and position lookups fail', () => {
    const fixture = open();
    http
      .expectOne(`${environment.apiUrl}/evaluations/periods`)
      .flush(null, { status: 500, statusText: 'x' });
    http
      .expectOne(`${environment.apiUrl}/employees/job-positions`)
      .flush(null, { status: 500, statusText: 'x' });
    flushList([item()]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(1);
  });

  it('opens the detail page of the clicked appeal by id', () => {
    const fixture = open();
    flushLookups();
    flushList([item()]);
    fixture.detectChanges();
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture.nativeElement.querySelector('tbody tr').click();
    expect(navigate).toHaveBeenCalledWith(['/appeals/department', item().id]);
  });

  it('shows an error with a retry button instead of fallback rows', () => {
    const fixture = open();
    flushLookups();
    // forkJoin cancels its sibling requests as soon as one of them fails.
    const requests = http.match((r) => r.url === url);
    requests[0].flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Không thể tải');
    expect(root.querySelector('tbody')).toBeNull();
    root.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    flushList([item()]);
    fixture.detectChanges();
    expect(root.textContent).toContain('9A8B7C6D');
  });

  it('shows the empty state when nothing needs action', () => {
    const fixture = open();
    flushLookups();
    flushList([], 0, 0);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.empty-state').textContent).toContain(
      'Không có khiếu nại nào cần xử lý',
    );
  });
});
