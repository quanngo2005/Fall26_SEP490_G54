import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router, RouterLink } from '@angular/router';
import { I18nService } from '../../../core/i18n/i18n.service';
import {
  currentWeekIndex,
  daysUntil,
  fill,
  niceAxisMax,
  openCount,
  previousRatedWeek,
  shortDatePattern,
} from './qa-dashboard-display';
import { dashboardFixture } from './qa-dashboard.fixtures.spec';
import { QaEmployeesComponent } from './qa-employees.component';
import { QaReasonsComponent } from './qa-reasons.component';
import { QaTrendChartComponent } from './qa-trend-chart.component';
import { By } from '@angular/platform-browser';

describe('QA dashboard display helpers', () => {
  it('fills placeholders and keeps unknown ones', () => {
    expect(fill('{a} / {b} {c}', { a: 1, b: 'x' })).toBe('1 / x {c}');
  });

  it('counts calendar days to the deadline', () => {
    const now = new Date(2026, 9, 26, 8, 15);
    expect(daysUntil(new Date(2026, 9, 31, 23, 59).toISOString(), now)).toBe(5);
    expect(daysUntil(new Date(2026, 9, 26, 23, 59).toISOString(), now)).toBe(0);
    expect(daysUntil(new Date(2026, 9, 25, 23, 59).toISOString(), now)).toBe(-1);
  });

  it('derives the day-month pattern of the locale', () => {
    expect(shortDatePattern('DD/MM/YYYY')).toBe('DD/MM');
    expect(shortDatePattern('MM/DD/YYYY')).toBe('MM/DD');
    expect(shortDatePattern('YYYY-MM-DD')).toBe('MM-DD');
  });

  it('rounds the axis up with head room', () => {
    expect(niceAxisMax(10)).toBe(15);
    expect(niceAxisMax(2)).toBe(3);
    expect(niceAxisMax(0)).toBe(3);
    expect(niceAxisMax(45)).toBe(60);
  });

  it('finds the current and the previous rated week', () => {
    const weeks = dashboardFixture().matchedRateChart.weeks;
    expect(openCount(weeks[7])).toBe(9);
    expect(currentWeekIndex(weeks, 'T43')).toBe(7);
    expect(currentWeekIndex(weeks, 'T99')).toBe(7);
    weeks[6].matchRate = null;
    expect(previousRatedWeek(weeks, 7)?.weekNumber).toBe(41);
    expect(previousRatedWeek(weeks, 0)).toBeNull();
  });
});

describe('QA dashboard blocks', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    TestBed.inject(I18nService).setLanguage('vi');
  });

  describe('QaReasonsComponent', () => {
    let fixture: ComponentFixture<QaReasonsComponent>;

    beforeEach(() => {
      fixture = TestBed.createComponent(QaReasonsComponent);
      fixture.componentRef.setInput('reasons', dashboardFixture().commonDiscrepancyReasons);
      fixture.componentRef.setInput('periodName', '10/2026');
      fixture.componentRef.setInput('linkState', { period: 'p10' });
      fixture.detectChanges();
    });

    const rows = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll('a.reason')];

    it('ranks the reasons by count with the dashboard wording', () => {
      expect(rows().map((r) => r.querySelector('.label')?.textContent?.trim())).toEqual([
        'Chưa có trong file đã import',
        'Sai nhân viên thực hiện',
        'Sai sản phẩm',
        'Lý do mới',
      ]);
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('18 mã · tháng 10/2026');
    });

    it('colours the import reason orange and the rest red, with matching legend counts', () => {
      expect(rows()[0].querySelector('.bar')?.classList).toContain('import');
      expect(rows()[1].querySelector('.bar')?.classList).not.toContain('import');
      const legend = (fixture.nativeElement as HTMLElement).querySelector('.legend')?.textContent;
      expect(legend).toContain('chờ import (12)');
      expect(legend).toContain('nhân viên cần sửa (6)');
    });

    it('opens the reconciliations filtered by the reason, with the period id kept out of the URL', () => {
      expect(rows()[1].getAttribute('href')).toBe('/reconciliations?reason=EMPLOYEE_MISMATCH');
      const link = fixture.debugElement.queryAll(By.css('a.reason'))[1];
      expect(link.injector.get(RouterLink).state).toEqual({ period: 'p10' });
    });

    it('says so when nothing is pending', () => {
      fixture.componentRef.setInput('reasons', []);
      fixture.detectChanges();
      expect(rows().length).toBe(0);
      expect((fixture.nativeElement as HTMLElement).querySelector('.qa-empty')).not.toBeNull();
    });
  });

  describe('QaEmployeesComponent', () => {
    let fixture: ComponentFixture<QaEmployeesComponent>;
    const el = () => fixture.nativeElement as HTMLElement;

    beforeEach(() => {
      fixture = TestBed.createComponent(QaEmployeesComponent);
      fixture.componentRef.setInput('employees', dashboardFixture().topDiscrepancyEmployees);
      fixture.componentRef.setInput('linkState', { period: 'p10' });
      fixture.detectChanges();
    });

    it('shows five employees a page with the shared pagination', () => {
      expect(el().querySelectorAll('tbody tr').length).toBe(5);
      expect(el().querySelector('.ui-pagination-range')?.textContent).toContain(
        '1–5 / 7 nhân viên',
      );
      const next = el().querySelector<HTMLButtonElement>('button[aria-label="Next page"]');
      next?.click();
      fixture.detectChanges();
      expect(el().querySelectorAll('tbody tr').length).toBe(2);
    });

    it('opens the unmatched codes of the employee from the row', () => {
      const link = el().querySelector('tbody a');
      expect(link?.getAttribute('href')).toBe('/reconciliations?status=NOT_MATCHED&q=NV020');
      expect(fixture.debugElement.query(By.css('tbody a')).injector.get(RouterLink).state).toEqual({
        period: 'p10',
      });
      const router = TestBed.inject(Router);
      const navigate = spyOn(router, 'navigate').and.resolveTo(true);
      (el().querySelector('tbody tr td.num') as HTMLElement).click();
      expect(navigate).toHaveBeenCalledWith(['/reconciliations'], {
        queryParams: { status: 'NOT_MATCHED', q: 'NV020' },
        state: { period: 'p10' },
      });
    });
  });

  describe('QaTrendChartComponent', () => {
    let fixture: ComponentFixture<QaTrendChartComponent>;
    const el = () => fixture.nativeElement as HTMLElement;

    beforeEach(() => {
      fixture = TestBed.createComponent(QaTrendChartComponent);
      fixture.componentRef.setInput('chart', dashboardFixture().matchedRateChart);
      fixture.detectChanges();
    });

    it('draws the unmatched codes per week from zero and highlights the current week', () => {
      const values = [...el().querySelectorAll('.value')].map((v) => v.textContent?.trim());
      expect(values).toEqual(['9', '8', '10', '6', '7', '10', '8', '9']);
      expect([...el().querySelectorAll('.grid span')].map((t) => t.textContent?.trim())).toEqual([
        '0',
        '5',
        '10',
        '15',
      ]);
      const current = el().querySelectorAll('.col.current');
      expect(current.length).toBe(1);
      expect(el().querySelector('.label.current .week')?.textContent?.trim()).toBe('T43');
      expect(el().querySelector('.label.current .rate')?.textContent?.trim()).toBe('khớp 95,6%');
    });

    it('marks the average and sums up the current week against the previous one', () => {
      expect(el().querySelector('.avg')?.textContent?.trim()).toBe('TB 8,4');
      expect(el().querySelector('.summary strong')?.textContent?.trim()).toBe(
        'Tuần 43: 9 / 204 mã chưa khớp',
      );
      expect(el().querySelector('.summary span')?.textContent?.trim()).toBe(
        'tỷ lệ khớp 95,6% · giảm 0,4 điểm so với tuần 42',
      );
    });

    it('shows an empty message when no week has transactions', () => {
      const chart = dashboardFixture().matchedRateChart;
      chart.weeks = chart.weeks.map((w) => ({
        ...w,
        matchedCount: 0,
        totalCount: 0,
        matchRate: null,
      }));
      fixture.componentRef.setInput('chart', chart);
      fixture.detectChanges();
      expect(el().querySelector('.chart')).toBeNull();
      expect(el().querySelector('.qa-empty')?.textContent).toContain('8 tuần');
    });
  });
});
