import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { AuthService } from '../../../core/auth.service';
import { AuthenticatedUser } from '../../../core/auth.models';
import { I18nService } from '../../../core/i18n/i18n.service';
import { QaDashboard, QaDashboardFilter } from '../../../core/qa-dashboard.models';
import { QaDashboardService } from '../../../core/qa-dashboard.service';
import { QaDashboardComponent } from './qa-dashboard.component';
import { dashboardFixture, filterOptionsFixture, inDays } from './qa-dashboard.fixtures.spec';

describe('QaDashboardComponent', () => {
  let fixture: ComponentFixture<QaDashboardComponent>;
  let response: () => Observable<QaDashboard>;
  let calls: QaDashboardFilter[];
  const user = signal<AuthenticatedUser | null>(null);

  function setup(
    data: QaDashboard | 'error' = dashboardFixture(),
    permissions = ['quality_flag:read'],
  ) {
    calls = [];
    response = () => (data === 'error' ? throwError(() => new Error('boom')) : of(data));
    user.set({ id: 'u', email: 'qa@example.test', fullName: 'QA', roles: ['QA'], permissions });
    TestBed.configureTestingModule({
      imports: [QaDashboardComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: user.asReadonly() } },
        {
          provide: QaDashboardService,
          useValue: {
            get: (filter: QaDashboardFilter) => {
              calls.push(filter);
              return response();
            },
            filters: () => of(filterOptionsFixture),
          },
        },
      ],
    });
    TestBed.inject(I18nService).setLanguage('vi');
    fixture = TestBed.createComponent(QaDashboardComponent);
    fixture.detectChanges();
  }

  const el = () => fixture.nativeElement as HTMLElement;
  const text = (selector: string) => el().querySelector(selector)?.textContent?.trim() ?? '';
  const cards = () => [...el().querySelectorAll<HTMLAnchorElement>('.cards a')];
  const pills = () => cards().map((c) => c.querySelector('.pill')?.textContent?.trim() ?? '');

  it('loads the dashboard for the current period when the URL has no filter', () => {
    setup();
    expect(calls).toEqual([{ periodId: null, departmentId: null, jobPositionId: null }]);
  });

  it('shows the four cards with their reminder pills and links', () => {
    setup();
    expect(cards().map((c) => c.querySelector('.count')?.textContent?.trim())).toEqual([
      '7',
      '12',
      '9',
      '3',
    ]);
    expect(pills()[0]).toBe('cũ nhất 4 ngày');
    expect(pills()[1]).toContain('hạn import');
    // Fields the API does not send yet are simply left out of the pill.
    expect(pills()[2]).toBe('5 mẫu · 4 cờ');
    expect(pills()[3]).toBe('−8 điểm');
    expect(cards()[0].getAttribute('href')).toBe('/reconciliations?period=p10&status=NOT_MATCHED');
    expect(cards()[1].getAttribute('href')).toBe('/reconciliations?period=p10&status=IN_REVIEW');
    expect(cards()[2].getAttribute('href')).toBe('/qa-queue');
    expect(cards()[3].getAttribute('href')).toBe('/risk-events');
  });

  it('adds the AI recordings and open risk events once the API sends them', () => {
    const data = dashboardFixture();
    data.kpiCards.qaQueue.aiSegmentCount = 5;
    data.kpiCards.riskEvents.openCount = 2;
    setup(data);
    expect(pills()[2]).toBe('5 mẫu · 4 cờ · 5 ghi âm AI');
    expect(pills()[3]).toBe('2 chưa đóng · −8 điểm');
  });

  it('greys out a card without work', () => {
    const data = dashboardFixture();
    data.kpiCards.notMatched = { count: 0, oldestDays: null, subtext: '' };
    setup(data);
    expect(cards()[0].classList).toContain('is-zero');
    expect(pills()[0]).toBe('');
  });

  it('warns about the lock deadline with the days left and the codes waiting for an import', () => {
    setup();
    const notice = text('.notice');
    expect(notice).toContain('Kỳ 10/2026 khoá KPI lúc 23:59');
    expect(notice).toContain('còn 5 ngày');
    expect(notice).toContain('12 mã giao dịch chưa có trong file import');
  });

  it('links to the import screen only with the import permission', () => {
    setup();
    expect(el().querySelector('.notice-link')).toBeNull();
    TestBed.resetTestingModule();
    setup(dashboardFixture(), ['quality_flag:read', 'transaction-import:manage']);
    expect(el().querySelector('.notice-link')?.getAttribute('href')).toBe('/data-import');
  });

  it('hides the notice while the deadline is more than a week away and nothing waits for an import', () => {
    const data = dashboardFixture();
    data.period.lockDeadline = inDays(12);
    data.banner.inReviewCount = 0;
    setup(data);
    expect(el().querySelector('.notice')).toBeNull();
  });

  it('says the period is locked instead of counting down', () => {
    const data = dashboardFixture();
    data.period.isLocked = true;
    setup(data);
    expect(text('.notice')).toContain('Kỳ 10/2026 đã khoá');
    expect(text('.notice')).not.toContain('còn');
  });

  it('shows an error with a retry instead of figures when the API fails', () => {
    setup('error');
    expect(el().querySelector('.cards')).toBeNull();
    expect(text('[role="alert"]')).toContain('Không tải được');
    response = () => of(dashboardFixture());
    (el().querySelector('.retry') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(calls.length).toBe(2);
    expect(cards().length).toBe(4);
  });

  it('keeps the filters in the URL and reloads with them', async () => {
    setup();
    const router = TestBed.inject(Router);
    await router.navigate([], { queryParams: { period: 'p09', department: 'd1', job: 'j1' } });
    fixture.detectChanges();
    expect(calls.at(-1)).toEqual({ periodId: 'p09', departmentId: 'd1', jobPositionId: 'j1' });
    expect(cards()[0].getAttribute('href')).toBe(
      '/reconciliations?period=p09&department=d1&job=j1&status=NOT_MATCHED',
    );
    (el().querySelector('.reset') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(router.url).toBe('/');
  });

  it('names monthly periods "Tháng …" in the period selector', () => {
    setup();
    expect(text('.filter.period .ui-select-value')).toBe('Tháng 10/2026');
  });

  it('follows the language switch', () => {
    setup();
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(pills()[0]).toBe('oldest 4 days');
    expect(text('.notice')).toContain('5 days left');
  });
});
