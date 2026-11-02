import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { NEVER, of, throwError } from 'rxjs';
import { BranchEvaluationListComponent } from './branch-evaluation-list.component';
import { EvaluationService } from '../../core/evaluation.service';
import {
  BranchEvaluationPeriodsOverview,
  FinalizePeriodResult,
} from '../../core/evaluation.models';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { I18nService } from '../../core/i18n/i18n.service';

@Component({ selector: 'app-header', standalone: true, template: '' })
class StubHeaderComponent {
  readonly title = input<string>();
}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class StubSidebarComponent {}

@Component({ selector: 'app-layout-icon', standalone: true, template: '' })
class StubIconComponent {
  readonly name = input<string>();
}

const mockOverview: BranchEvaluationPeriodsOverview = {
  branchName: 'Chi nhánh kiểm thử',
  branchCode: 'BR-TEST',
  availableYears: [2026, 2025],
  periods: [
    {
      id: 'p-q3',
      code: 'Q3-2026',
      name: 'Quý 3/2026',
      startDate: '2026-07-01',
      endDate: '2026-09-30',
      periodType: 'QUARTER',
      status: 'IN_REVIEW',
      displayStatus: 'IN_REVIEW',
      qualityRatio: null,
      submittedDepartmentCount: 6,
      approvedDepartmentCount: 4,
      totalDepartmentCount: 8,
      appealWindow: null,
      canOpen: true,
      canLock: false,
      canViewHistory: false,
    },
    {
      id: 'p-q2',
      code: 'Q2-2026',
      name: 'Quý 2/2026',
      startDate: '2026-04-01',
      endDate: '2026-06-30',
      periodType: 'QUARTER',
      status: 'IN_REVIEW',
      displayStatus: 'PUBLISHED',
      qualityRatio: null,
      submittedDepartmentCount: 8,
      approvedDepartmentCount: 8,
      totalDepartmentCount: 8,
      appealWindow: null,
      canOpen: false,
      canLock: true,
      canViewHistory: false,
    },
    {
      id: 'p-q1',
      code: 'Q1-2026',
      name: 'Quý 1/2026',
      startDate: '2026-01-01',
      endDate: '2026-03-31',
      periodType: 'QUARTER',
      status: 'LOCKED',
      displayStatus: 'LOCKED',
      qualityRatio: null,
      submittedDepartmentCount: 8,
      approvedDepartmentCount: 8,
      totalDepartmentCount: 8,
      appealWindow: null,
      canOpen: false,
      canLock: false,
      canViewHistory: true,
    },
    {
      id: 'p-q4',
      code: 'Q4-2026',
      name: 'Quý 4/2026',
      startDate: '2026-10-01',
      endDate: '2026-12-31',
      periodType: 'QUARTER',
      status: 'OPEN',
      displayStatus: 'IN_PROGRESS',
      qualityRatio: null,
      submittedDepartmentCount: 0,
      approvedDepartmentCount: 0,
      totalDepartmentCount: 8,
      appealWindow: null,
      canOpen: false,
      canLock: false,
      canViewHistory: false,
    },
  ],
};

describe('BranchEvaluationListComponent', () => {
  let component: BranchEvaluationListComponent;
  let fixture: ComponentFixture<BranchEvaluationListComponent>;
  let api: jasmine.SpyObj<EvaluationService>;
  let router: Router;
  let i18n: I18nService;
  const currentYear = new Date().getFullYear();

  const html = (): HTMLElement => fixture.nativeElement;

  beforeEach(async () => {
    api = jasmine.createSpyObj<EvaluationService>('EvaluationService', [
      'branchPeriods',
      'finalize',
    ]);
    api.branchPeriods.and.returnValue(of(mockOverview));

    await TestBed.configureTestingModule({
      imports: [BranchEvaluationListComponent],
      providers: [provideRouter([]), { provide: EvaluationService, useValue: api }],
    })
      .overrideComponent(BranchEvaluationListComponent, {
        remove: {
          imports: [HeaderComponent, SidebarComponent, LayoutIconComponent],
        },
        add: {
          imports: [StubHeaderComponent, StubSidebarComponent, StubIconComponent],
        },
      })
      .compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigate');
    i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');

    fixture = TestBed.createComponent(BranchEvaluationListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    i18n.setLanguage('vi');
  });

  it('loads the periods for the current year with no other filter', () => {
    expect(api.branchPeriods).toHaveBeenCalledOnceWith(currentYear, 'ALL', 'ALL');
  });

  it('renders the branch name returned by the API in the title', () => {
    expect(html().querySelector('.page-title')?.textContent).toContain(
      'Kỳ đánh giá · Chi nhánh kiểm thử',
    );
    expect(html().querySelector('.page-subtitle')?.textContent).toContain('Duyệt xếp hạng phòng');
  });

  it('shows a placeholder instead of an invented branch name while none is known', () => {
    api.branchPeriods.and.returnValue(NEVER);
    fixture = TestBed.createComponent(BranchEvaluationListComponent);
    fixture.detectChanges();

    const title = fixture.nativeElement.querySelector('.page-title')?.textContent ?? '';
    expect(title).not.toContain('Hà Đông');
    expect(title).toContain('Kỳ đánh giá');
  });

  it('renders one row per API period with the counts it returned', () => {
    const rows = html().querySelectorAll('tbody tr');
    expect(rows.length).toBe(4);

    const q3Row = rows[0];
    expect(q3Row.querySelector('.period-name')?.textContent).toContain('Quý 3/2026');
    expect(q3Row.querySelector('.cell-submitted')?.textContent).toContain('6/8');
    expect(q3Row.querySelector('.cell-approved')?.textContent).toContain('4/8');
    expect(q3Row.querySelector('.badge-in-review')?.textContent).toContain('Đang duyệt xếp hạng');
    expect(q3Row.querySelector('.btn-primary')?.textContent).toContain('Mở ›');

    const q2Row = rows[1];
    expect(q2Row.querySelector('.badge-published')?.textContent).toContain('Published');
    expect(q2Row.querySelector('.btn-lock')?.textContent).toContain('Khóa kỳ');

    const q1Row = rows[2];
    expect(q1Row.querySelector('.badge-locked')?.textContent).toContain('LOCKED');
    expect(q1Row.querySelector('.btn-link')?.textContent).toContain('Xem ›');
  });

  it('shows a dash for quality ratio and appeal window when the API has no value', () => {
    const empty = i18n.localization.evaluations.branchPeriods.notAvailable;
    for (const row of Array.from(html().querySelectorAll('tbody tr'))) {
      expect(row.querySelector('.cell-ratio')?.textContent?.trim()).toBe(empty);
      expect(row.querySelector('.cell-appeal .dash-text')?.textContent?.trim()).toBe(empty);
    }
  });

  it('formats period dates as day/month/year', () => {
    const time = html().querySelector('tbody tr .cell-time')?.textContent ?? '';
    expect(time).toContain('01/07/2026');
    expect(time).toContain('30/09/2026');
  });

  it('offers the years from the API plus the selected year, newest first', () => {
    expect(component.availableYears()).toEqual(
      Array.from(new Set([2026, 2025, currentYear])).sort((a, b) => b - a),
    );
  });

  it('sends every filter change to the API instead of filtering in the browser', () => {
    api.branchPeriods.calls.reset();

    component.onStatusChange('IN_REVIEW');
    expect(api.branchPeriods).toHaveBeenCalledWith(currentYear, 'ALL', 'IN_REVIEW');

    component.onPeriodTypeChange('QUARTER');
    expect(api.branchPeriods).toHaveBeenCalledWith(currentYear, 'QUARTER', 'IN_REVIEW');

    component.onYearChange('2025');
    expect(api.branchPeriods).toHaveBeenCalledWith(2025, 'QUARTER', 'IN_REVIEW');

    component.onYearChange('ALL');
    expect(api.branchPeriods).toHaveBeenCalledWith(null, 'QUARTER', 'IN_REVIEW');
    expect(api.branchPeriods).toHaveBeenCalledTimes(4);
  });

  it('renders exactly what the API returns for a filter', () => {
    api.branchPeriods.and.returnValue(of({ ...mockOverview, periods: [mockOverview.periods[0]] }));
    component.onStatusChange('IN_REVIEW');
    fixture.detectChanges();

    expect(html().querySelectorAll('tbody tr').length).toBe(1);
  });

  it('shows the empty state when the API returns no period', () => {
    api.branchPeriods.and.returnValue(of({ ...mockOverview, periods: [] }));
    component.loadData();
    fixture.detectChanges();

    expect(html().querySelector('.empty-state')?.textContent).toContain(
      'Không tìm thấy kỳ đánh giá nào',
    );
    expect(html().querySelector('tbody')).toBeNull();
  });

  const errorCases: [string, number, string][] = [
    ['offline', 0, 'Không có kết nối mạng'],
    ['forbidden', 403, 'Bạn không có quyền xem màn hình này'],
    ['generic', 500, 'Không thể tải danh sách kỳ đánh giá'],
  ];
  for (const [name, status, text] of errorCases) {
    it(`shows the ${name} error (HTTP ${status}) with no fake rows`, () => {
      api.branchPeriods.and.returnValue(throwError(() => new HttpErrorResponse({ status })));
      component.loadData();
      fixture.detectChanges();

      expect(html().querySelector('.error-state .error-msg')?.textContent).toContain(text);
      expect(html().querySelectorAll('tbody tr').length).toBe(0);
      expect(component.periods()).toEqual([]);
    });
  }

  it('retries the request from the error state', () => {
    api.branchPeriods.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.loadData();
    fixture.detectChanges();

    api.branchPeriods.and.returnValue(of(mockOverview));
    (html().querySelector('.error-state button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(html().querySelector('.error-state')).toBeNull();
    expect(html().querySelectorAll('tbody tr').length).toBe(4);
  });

  it('navigates to the period evaluations when clicking Open', () => {
    const period = mockOverview.periods[0];
    component.openPeriod(period);
    expect(router.navigate).toHaveBeenCalledWith(['/evaluations'], {
      queryParams: { periodId: period.id },
    });
  });

  it('navigates to the period evaluations when clicking View', () => {
    const period = mockOverview.periods[2];
    component.viewHistory(period);
    expect(router.navigate).toHaveBeenCalledWith(['/evaluations'], {
      queryParams: { periodId: period.id },
    });
  });

  it('opens and cancels the finalize lock modal', () => {
    const period = mockOverview.periods[1];
    expect(component.isLockModalOpen()).toBeFalse();

    component.openLockModal(period);
    expect(component.isLockModalOpen()).toBeTrue();
    expect(component.periodToLock()?.id).toBe(period.id);

    component.closeLockModal();
    expect(component.isLockModalOpen()).toBeFalse();
    expect(component.periodToLock()).toBeNull();
  });

  it('localizes the close button label of the lock modal', () => {
    component.openLockModal(mockOverview.periods[1]);
    fixture.detectChanges();
    expect(html().querySelector('.btn-close')?.getAttribute('aria-label')).toBe('Đóng');

    i18n.setLanguage('en');
    fixture.detectChanges();
    expect(html().querySelector('.btn-close')?.getAttribute('aria-label')).toBe('Close');
  });

  it('submits lock finalization and reloads data on success', () => {
    const period = mockOverview.periods[1];
    const finalizeRes: FinalizePeriodResult = {
      periodId: period.id,
      periodStatus: 'LOCKED',
      finalizedAt: '2026-10-15T10:00:00Z',
      totalEvaluationsFinalized: 92,
      message: 'Locked',
    };
    api.finalize.and.returnValue(of(finalizeRes));
    api.branchPeriods.calls.reset();

    component.openLockModal(period);
    component.lockSignatureToken.set('PIN-1234');
    component.lockApprovalComments.set('Duyệt kết quả toàn chi nhánh.');
    component.confirmLockPeriod();

    expect(api.finalize).toHaveBeenCalledWith({
      periodId: period.id,
      signatureToken: 'PIN-1234',
      approvalComments: 'Duyệt kết quả toàn chi nhánh.',
    });
    expect(component.isLockModalOpen()).toBeFalse();
    expect(component.toastMessage()).toContain('thành công');
    expect(api.branchPeriods).toHaveBeenCalledTimes(1);
  });

  it('displays the dispute error when the period has pending disputes (BR-31)', () => {
    const period = mockOverview.periods[1];
    api.finalize.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { errorCode: 'EVALUATION_DISPUTE_PENDING' },
          }),
      ),
    );

    component.openLockModal(period);
    component.confirmLockPeriod();

    expect(component.lockError()).toContain('BR-31');
    expect(component.isLockModalOpen()).toBeTrue();
    expect(component.isLocking()).toBeFalse();
  });

  it('displays the generic lock error and keeps the modal open on other failures', () => {
    api.finalize.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    component.openLockModal(mockOverview.periods[1]);
    component.confirmLockPeriod();

    expect(component.lockError()).toContain('Không thể khóa kỳ');
    expect(component.isLockModalOpen()).toBeTrue();
  });

  it('renders the period lifecycle guide stepper track', () => {
    const stepperCard = html().querySelector('.lifecycle-card');
    expect(stepperCard).not.toBeNull();
    const steps = stepperCard?.querySelectorAll('.step-box');
    expect(steps?.length).toBe(5);
    expect(steps?.[0].textContent).toContain('Đang diễn ra');
    expect(steps?.[2].textContent).toContain('Đang duyệt xếp hạng');
    expect(steps?.[4].textContent).toContain('LOCKED');
  });

  it('reactively updates text and date format when switching language to English', () => {
    i18n.setLanguage('en');
    fixture.detectChanges();

    expect(html().querySelector('.page-title')?.textContent).toContain(
      'Evaluation Periods · Chi nhánh kiểm thử',
    );
    expect(html().querySelector('.th-period')?.textContent).toContain('PERIOD');
  });
});
