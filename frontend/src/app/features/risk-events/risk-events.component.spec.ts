import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, input, output, signal } from '@angular/core';
import { fakeAsync, TestBed, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthenticatedUser } from '../../core/auth.models';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { RiskEventList, RiskEventListItem } from '../../core/risk-event.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { RiskEventCreateModalComponent } from './risk-event-create-modal.component';
import { RiskEventDrawerComponent } from './risk-event-drawer.component';
import { RiskEventsComponent } from './risk-events.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}
@Component({ selector: 'app-risk-event-drawer', template: '' })
class DrawerStub {
  readonly eventId = input<string>();
  readonly closed = output<void>();
  readonly reversed = output<string>();
}
@Component({ selector: 'app-risk-event-create-modal', template: '' })
class ModalStub {
  readonly created = output<{ code: string; uploadFailed: boolean }>();
  readonly dismissed = output<void>();
}

const url = `${environment.apiUrl}/risk-events`;
const item = (over: Partial<RiskEventListItem> = {}): RiskEventListItem => ({
  id: 'r1',
  code: 'RSK-0045',
  occurredAt: '2026-10-22T12:00:00Z',
  employeeId: 'e1',
  employeeCode: 'NV0310',
  employeeName: 'T.Q.Bảo',
  departmentId: 'd1',
  departmentName: 'KHCN',
  eventType: 'WRONG_CUSTOMER_INFO',
  severity: 'MEDIUM',
  penaltyPoints: 3,
  workId: 'w1',
  workCode: 'WK-1051',
  status: 'CONFIRMED',
  state: 'RECORDED',
  createdAt: '2026-10-23T02:30:12Z',
  ...over,
});
const list = (items: RiskEventListItem[], over: Partial<RiskEventList> = {}) => ({
  success: true,
  status: 200,
  data: {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 20,
    counts: { recorded: 3, reversed: 1 },
    departments: [{ id: 'd1', name: 'KHCN' }],
    ...over,
  },
});

describe('RiskEventsComponent', () => {
  let http: HttpTestingController;
  const user = signal<AuthenticatedUser | null>(null);

  const as = (permissions: string[]) =>
    user.set({ id: 'u1', email: 'u@test.local', fullName: 'QA', roles: ['QA'], permissions });
  const open = () => {
    const fixture = TestBed.createComponent(RiskEventsComponent);
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/evaluations/periods`).flush({
      success: true,
      status: 200,
      data: [{ id: 'p1', code: '2026-10', name: '10/2026', status: 'OPEN' }],
    });
    return fixture;
  };
  const listRequest = () => http.expectOne((r) => r.url === url);
  const text = (fixture: { nativeElement: HTMLElement }) =>
    (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
  const choose = (select: HTMLSelectElement, value: string) => {
    select.value = value;
    select.dispatchEvent(new Event('change'));
  };

  beforeEach(async () => {
    user.set(null);
    as(['RISK_EVENT_READ', 'RISK_EVENT_CREATE']);
    await TestBed.configureTestingModule({
      imports: [RiskEventsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: user.asReadonly() } },
      ],
    })
      .overrideComponent(RiskEventsComponent, {
        remove: {
          imports: [
            HeaderComponent,
            SidebarComponent,
            RiskEventDrawerComponent,
            RiskEventCreateModalComponent,
          ],
        },
        add: { imports: [HeaderStub, SidebarStub, DrawerStub, ModalStub] },
      })
      .compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('lists the events with the penalty in red, the link and the status', () => {
    const fixture = open();
    listRequest().flush(
      list([
        item(),
        item({
          id: 'r2',
          code: 'RSK-0041',
          state: 'REVERSED',
          status: 'DISMISSED',
          workCode: null,
        }),
      ]),
    );
    fixture.detectChanges();

    const body = text(fixture);
    expect(body).toContain('RSK-0045');
    expect(body).toContain('22/10/2026');
    expect(body).toContain('NV0310 · T.Q.Bảo');
    expect(body).toContain('KHCN');
    expect(body).toContain('Khai sai thông tin khách hàng');
    expect(body).toContain('Trung bình');
    expect(body).toContain('−3');
    expect(body).toContain('WK-1051');
    expect(body).toContain('Đã ghi');
    expect(body).toContain('Đã gỡ');
    expect(body).toContain('1–2 / 2 sự kiện');
    const penalties = fixture.nativeElement.querySelectorAll('.penalty');
    expect(penalties[0].classList.contains('muted')).toBeFalse();
    expect(penalties[1].classList.contains('muted')).toBeTrue();
  });

  it('shows an unknown event type as it was stored', () => {
    const fixture = open();
    listRequest().flush(list([item({ eventType: 'FRAUD' })]));
    fixture.detectChanges();

    expect(text(fixture)).toContain('FRAUD');
  });

  it('shows the record button only to who may record', () => {
    const withCreate = open();
    listRequest().flush(list([item()]));
    withCreate.detectChanges();
    expect(text(withCreate)).toContain('Ghi sự kiện rủi ro');
    expect(withCreate.nativeElement.querySelector('.create-btn')).not.toBeNull();
  });

  it('hides the record button from a read-only viewer', () => {
    as(['RISK_EVENT_READ']);
    const fixture = open();
    listRequest().flush(list([item()]));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.create-btn')).toBeNull();
  });

  it('sends the period, department, severity and status filters from the first page', () => {
    const fixture = open();
    listRequest().flush(list([item()]));
    fixture.detectChanges();
    const selects = fixture.nativeElement.querySelectorAll(
      'select',
    ) as NodeListOf<HTMLSelectElement>;
    expect(selects[1].textContent).toContain('KHCN');
    expect(selects[0].textContent).toContain('2026-10');

    choose(selects[0], 'p1');
    expect(listRequest().request.params.get('periodId')).toBe('p1');
    choose(selects[1], 'd1');
    http.match((r) => r.url === url);
    choose(selects[2], 'HIGH');
    http.match((r) => r.url === url);
    choose(selects[3], 'REVERSED');
    const last = listRequest();
    expect(last.request.params.get('periodId')).toBe('p1');
    expect(last.request.params.get('departmentId')).toBe('d1');
    expect(last.request.params.get('severity')).toBe('HIGH');
    expect(last.request.params.get('state')).toBe('REVERSED');
    expect(last.request.params.get('page')).toBe('1');
    last.flush(list([]));
  });

  it('waits for the typing to stop before it searches', fakeAsync(() => {
    const fixture = TestBed.createComponent(RiskEventsComponent);
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/evaluations/periods`).flush({
      success: true,
      status: 200,
      data: [],
    });
    listRequest().flush(list([item()]));
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[type="text"]') as HTMLInputElement;
    for (const value of ['R', 'RS', 'RSK-0045']) {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      tick(100);
    }
    http.expectNone((r) => r.url === url);
    tick(300);
    const request = listRequest();
    expect(request.request.params.get('search')).toBe('RSK-0045');
    request.flush(list([item()]));
  }));

  it('pages through the events', () => {
    const fixture = open();
    listRequest().flush(list([item()], { totalCount: 45, pageSize: 20 }));
    fixture.detectChanges();

    const pages = fixture.nativeElement.querySelectorAll(
      '.page-box',
    ) as NodeListOf<HTMLButtonElement>;
    expect(pages.length).toBe(3);
    pages[2].click();
    expect(listRequest().request.params.get('page')).toBe('3');
  });

  it('opens the drawer of a row and moves it with the arrow keys', () => {
    const fixture = open();
    listRequest().flush(list([item(), item({ id: 'r2', code: 'RSK-0044' })]));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-risk-event-drawer')).toBeNull();

    (fixture.nativeElement.querySelector('tbody tr') as HTMLElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-risk-event-drawer')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('tbody tr')[1].classList).toContain(
      'selected-row',
    );
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('tbody tr')[0].classList).toContain(
      'selected-row',
    );
  });

  it('does not move the drawer while the user types in a field', () => {
    const fixture = open();
    listRequest().flush(list([item(), item({ id: 'r2' })]));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('tbody tr') as HTMLElement).click();
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[type="text"]') as HTMLInputElement;
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('tbody tr')[0].classList).toContain(
      'selected-row',
    );
  });

  it('opens the record modal and reports the result after it was saved', () => {
    const fixture = open();
    listRequest().flush(list([item()]));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.create-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    const modal = fixture.nativeElement.querySelector('app-risk-event-create-modal');
    expect(modal).not.toBeNull();

    fixture.componentInstance['onCreated']({ code: 'RSK-0046', uploadFailed: false });
    fixture.detectChanges();
    listRequest().flush(list([item()]));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-risk-event-create-modal')).toBeNull();
    expect(text(fixture)).toContain('Đã ghi sự kiện RSK-0046.');
  });

  it('warns when the attachments of a recorded event could not be uploaded', () => {
    const fixture = open();
    listRequest().flush(list([item()]));
    fixture.componentInstance['onCreated']({ code: 'RSK-0046', uploadFailed: true });
    listRequest().flush(list([item()]));
    fixture.detectChanges();

    expect(text(fixture)).toContain('chưa tải được tệp đính kèm');
  });

  it('announces a reversal and refreshes the list', () => {
    const fixture = open();
    listRequest().flush(list([item()]));
    fixture.componentInstance['onReversed']('RSK-0045');
    listRequest().flush(list([item({ state: 'REVERSED' })]));
    fixture.detectChanges();

    expect(text(fixture)).toContain('Đã gỡ điểm phạt của sự kiện RSK-0045.');
  });

  it('keeps the filters usable when the periods cannot be read, and retries a failed list', () => {
    const fixture = TestBed.createComponent(RiskEventsComponent);
    fixture.detectChanges();
    http
      .expectOne(`${environment.apiUrl}/evaluations/periods`)
      .flush('', { status: 403, statusText: 'Forbidden' });
    listRequest().flush('', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(text(fixture)).toContain('Không tải được danh sách sự kiện rủi ro');
    (fixture.nativeElement.querySelector('.state-block button') as HTMLButtonElement).click();
    listRequest().flush(list([item()]));
    fixture.detectChanges();
    expect(text(fixture)).toContain('RSK-0045');
  });

  const periodsContaining = (today: Date) => {
    const iso = (d: Date) =>
      `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    const around = (days: number) =>
      iso(new Date(today.getFullYear(), today.getMonth(), today.getDate() + days));
    return [
      {
        id: 'year',
        code: 'Y',
        name: 'Year',
        startDate: iso(new Date(today.getFullYear(), 0, 1)),
        endDate: iso(new Date(today.getFullYear(), 11, 31)),
        status: 'OPEN',
      },
      {
        id: 'month',
        code: 'M',
        name: 'Month',
        startDate: around(-1),
        endDate: around(1),
        status: 'OPEN',
      },
    ];
  };

  it('opens on the current period, newest first, instead of every period', async () => {
    const fixture = TestBed.createComponent(RiskEventsComponent);
    fixture.detectChanges();
    http
      .expectOne(`${environment.apiUrl}/evaluations/periods`)
      .flush({ success: true, status: 200, data: periodsContaining(new Date()) });
    fixture.detectChanges();

    const request = listRequest();
    expect(request.request.params.get('periodId')).toBe('month');
    request.flush(list([]));
    fixture.detectChanges();
    const options = Array.from(
      fixture.nativeElement.querySelectorAll(
        '.period-select option',
      ) as NodeListOf<HTMLOptionElement>,
    );
    expect(options.map((o) => o.value)).toEqual(['ALL', 'month', 'year']);
    await fixture.whenStable();
    expect(
      (fixture.nativeElement.querySelector('.period-select select') as HTMLSelectElement).value,
    ).toBe('month');
  });

  it('sorts by the clicked header, reverses on a second click and starts over at the first page', () => {
    const fixture = open();
    listRequest().flush(list([item()]));
    fixture.detectChanges();
    const header = (label: string) =>
      (Array.from(fixture.nativeElement.querySelectorAll('th')) as HTMLElement[]).find((th) =>
        th.textContent?.includes(label),
      );
    expect(header('NGÀY VI')?.getAttribute('aria-sort') === 'descending').toBeTrue();

    (header('MÃ')?.querySelector('.sort-btn') as HTMLButtonElement).click();
    const first = listRequest();
    expect(first.request.params.get('sortBy')).toBe('code');
    expect(first.request.params.get('sortDir')).toBe('asc');
    expect(first.request.params.get('page')).toBe('1');
    first.flush(list([item()]));
    fixture.detectChanges();
    expect(header('MÃ')?.getAttribute('aria-sort')).toBe('ascending');

    (header('MÃ')?.querySelector('.sort-btn') as HTMLButtonElement).click();
    const second = listRequest();
    expect(second.request.params.get('sortDir')).toBe('desc');
    second.flush(list([item()]));
    fixture.detectChanges();
    expect(header('MÃ')?.getAttribute('aria-sort')).toBe('descending');

    (header('ĐIỂM PHẠT')?.querySelector('.sort-btn') as HTMLButtonElement).click();
    const date = listRequest();
    expect(date.request.params.get('sortBy')).toBe('penalty');
    expect(date.request.params.get('sortDir')).toBe('desc');
    date.flush(list([item()]));
  });
});
