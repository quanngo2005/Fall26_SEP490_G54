import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AppealListItem } from '../../core/appeal.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { MyAppealsComponent } from './my-appeals.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const url = `${environment.apiUrl}/appeals`;
const item = (over: Partial<AppealListItem> = {}): AppealListItem => ({
  id: '3f2a9c10-1111-2222-3333-444455556666',
  resultSubmissionId: 's1',
  workId: 'w1',
  workCode: 'WRK-1',
  workTitle: 'Mở thẻ',
  submissionNo: 1,
  employeeId: 'e1',
  employeeCode: 'NV001',
  employeeName: 'Nhân viên',
  status: 'OPEN',
  filedAt: '2026-10-24T10:00:00+00:00',
  decidedAt: null,
  updatedAt: '2026-10-24T10:00:00+00:00',
  type: 'RESULT',
  performanceEvaluationId: null,
  periodCode: null,
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

describe('MyAppealsComponent', () => {
  let http: HttpTestingController;
  const open = () => {
    const fixture = TestBed.createComponent(MyAppealsComponent);
    fixture.detectChanges();
    return fixture;
  };
  /** The page loads the periods, then the list plus four count requests. */
  const flushInitial = (list: AppealListItem[], counts = [list.length, 0, 0, 0]) => {
    http.expectOne(`${environment.apiUrl}/evaluations/periods`).flush({
      success: true,
      status: 200,
      data: [{ id: 'p1', code: '2026-Q2', name: 'Q2', status: 'OPEN' }],
    });
    const requests = http.match((r) => r.url === url);
    expect(requests.length).toBe(5);
    for (const request of requests) {
      expect(request.request.params.get('scope')).toBe('MINE');
      const size = request.request.params.get('pageSize');
      const status = request.request.params.get('status');
      if (size === '1') {
        const index =
          status === null ? 0 : status === 'OPEN' ? 1 : status === 'UNDER_REVIEW' ? 2 : 3;
        request.flush(page([], counts[index]));
      } else {
        request.flush(page(list));
      }
    }
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyAppealsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    })
      .overrideComponent(MyAppealsComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('lists only the caller own appeals from the API and shows the real data', () => {
    const fixture = open();
    flushInitial([item()], [3, 2, 1, 0]);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.textContent).toContain('3F2A9C10');
    expect(root.textContent).toContain('WRK-1 · v1');
    expect(root.textContent).not.toContain('APL-0412');
    const counts = Array.from(root.querySelectorAll('.tab-count')).map((e) =>
      e.textContent?.trim(),
    );
    expect(counts).toEqual(['3', '2', '1', '0']);
  });

  it('shows N/A for a handler and outcome that the API does not have yet', () => {
    const fixture = open();
    flushInitial([item()]);
    fixture.detectChanges();
    const cells = fixture.nativeElement.querySelectorAll('tbody td');
    // Columns: code, type, subject, filed on, handler, status, outcome.
    expect(cells[4].textContent.trim()).toBe('N/A');
    expect(cells[6].textContent.trim()).toBe('N/A');
  });

  it('shows an error with a retry button instead of fallback rows', () => {
    const fixture = open();
    http
      .expectOne(`${environment.apiUrl}/evaluations/periods`)
      .flush([], { status: 500, statusText: 'x' });
    // forkJoin cancels its sibling requests as soon as one of them fails.
    const requests = http.match((r) => r.url === url);
    requests[0].flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Không thể tải');
    expect(root.querySelector('tbody')).toBeNull();
    root.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    for (const request of http.match((r) => r.url === url)) request.flush(page([item()]));
    fixture.detectChanges();
    expect(root.textContent).toContain('3F2A9C10');
  });

  it('reloads with the status of the chosen tab', () => {
    const fixture = open();
    flushInitial([]);
    fixture.detectChanges();
    const tabs = fixture.nativeElement.querySelectorAll('.tab-btn');
    tabs[3].click();
    const requests = http.match((r) => r.url === url);
    const main = requests.find((r) => r.request.params.get('pageSize') !== '1');
    expect(main?.request.params.get('status')).toBe('ACCEPTED,REJECTED,WITHDRAWN');
    expect(main?.request.params.get('scope')).toBe('MINE');
    for (const request of requests) request.flush(page([]));
  });

  it('links to the department queue', () => {
    const fixture = open();
    flushInitial([]);
    fixture.detectChanges();
    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('.view-switch-link');
    expect(link.getAttribute('href')).toBe('/appeals/department');
  });

  it('opens the drawer with the detail of the clicked appeal and withdraws only an open one', () => {
    const fixture = open();
    flushInitial([item()]);
    fixture.detectChanges();
    fixture.nativeElement.querySelector('tbody tr').click();
    const id = item().id;
    http.expectOne(`${url}/${id}`).flush({
      success: true,
      status: 200,
      data: { ...item(), reason: 'Lý do khiếu nại', evidences: [], scoreDisputeReason: null },
    });
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('app-my-appeal-drawer')?.textContent).toContain('Lý do khiếu nại');
    const withdraw = root.querySelector<HTMLButtonElement>('.withdraw-button');
    expect(withdraw?.disabled).toBeFalse();
    spyOn(window, 'confirm').and.returnValue(true);
    withdraw?.click();
    http.expectOne(`${url}/${id}/withdraw`).flush({ success: true, status: 200, data: item() });
    for (const request of http.match((r) => r.url === url)) request.flush(page([]));
    http.expectOne(`${url}/${id}`).flush({
      success: true,
      status: 200,
      data: {
        ...item({ status: 'WITHDRAWN' }),
        reason: 'x',
        evidences: [],
        scoreDisputeReason: null,
      },
    });
  });

  it('keeps the drawer button disabled for an appeal that is no longer open', () => {
    const fixture = open();
    flushInitial([item({ status: 'ACCEPTED', note: 'Chấp nhận do đủ chứng từ' })]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('tbody').textContent).toContain(
      'Chấp nhận do đủ chứng từ',
    );
    fixture.nativeElement.querySelector('tbody tr').click();
    http.expectOne(`${url}/${item().id}`).flush({
      success: true,
      status: 200,
      data: {
        ...item({ status: 'ACCEPTED' }),
        reason: 'x',
        evidences: [],
        scoreDisputeReason: null,
      },
    });
    fixture.detectChanges();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.withdraw-button')
        ?.disabled,
    ).toBeTrue();
  });

  it('shows the error of a failed withdrawal by its error code', () => {
    const fixture = open();
    flushInitial([item()]);
    fixture.detectChanges();
    fixture.nativeElement.querySelector('tbody tr').click();
    http.expectOne(`${url}/${item().id}`).flush({
      success: true,
      status: 200,
      data: { ...item(), reason: 'x', evidences: [], scoreDisputeReason: null },
    });
    fixture.detectChanges();
    spyOn(window, 'confirm').and.returnValue(true);
    fixture.nativeElement.querySelector('.withdraw-button').click();
    http
      .expectOne(`${url}/${item().id}/withdraw`)
      .flush({ errorCode: 'APPEAL_INVALID_STATE' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.alert-error')?.textContent).toContain(
      'không còn ở trạng thái',
    );
  });

  it('switches language live without changing the data', () => {
    const fixture = open();
    flushInitial([item()]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Khiếu nại của tôi');
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('My appeals');
    expect(fixture.nativeElement.textContent).toContain('3F2A9C10');
  });
});
