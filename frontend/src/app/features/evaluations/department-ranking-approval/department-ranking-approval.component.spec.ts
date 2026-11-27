import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { DepartmentRankingApprovalComponent } from './department-ranking-approval.component';
import { EvaluationService } from '../../../core/evaluation.service';
import { KpiDefinitionService } from '../../../core/kpi-definition.service';
import { I18nService } from '../../../core/i18n/i18n.service';
import { Localization } from '../../../core/i18n/i18n.models';
import {
  BranchApprovalOverview,
  CalibrationOverview,
  DepartmentApprovalResult,
  CalibrationDecisionResult,
  FinalizePeriodResult,
} from '../../../core/evaluation.models';
import { HeaderComponent, SidebarComponent } from '../../../shared/layout';
import { LayoutIconComponent } from '../../../shared/layout/layout-icon.component';
import { provideRouteState } from '../../../core/route-state.testing';

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

describe('DepartmentRankingApprovalComponent', () => {
  let component: DepartmentRankingApprovalComponent;
  let fixture: ComponentFixture<DepartmentRankingApprovalComponent>;
  let evaluationServiceSpy: jasmine.SpyObj<EvaluationService>;
  let i18nServiceSpy: jasmine.SpyObj<I18nService>;
  let router: Router;

  const mockOverview: BranchApprovalOverview = {
    period: {
      id: 'p-1',
      code: 'Q3-2026',
      name: 'Quý 3/2026 - Chi nhánh Quận 1',
      periodType: 'QUARTER',
      status: 'IN_REVIEW',
      startDate: '2026-07-01',
      endDate: '2099-12-31',
    },
    totalEmployees: 20,
    totalEvaluated: 20,
    totalTierACount: 2,
    branchTierAPercent: 10,
    branchCapPercent: 10,
    isBranchCapCompliant: true,
    canFinalize: false,
    disputedPendingCount: 1,
    departments: [
      {
        departmentId: 'd-1',
        departmentName: 'Phòng Khách hàng Cá nhân',
        departmentCode: 'KHCN',
        totalEmployees: 12,
        evaluatedEmployees: 12,
        status: 'IN_REVIEW',
        submittedAt: '2026-10-02T10:00:00Z',
        managerName: 'Trưởng phòng A',
        averageScore: 88.5,
        tierACount: 1,
        tierAPercent: 8.3,
        tierBCount: 6,
        tierCCount: 4,
        tierDCount: 1,
        isQuotaCompliant: true,
        reviewerComment: null,
      },
      {
        departmentId: 'd-2',
        departmentName: 'Phòng Khách hàng Doanh nghiệp',
        departmentCode: 'KHDN',
        totalEmployees: 8,
        evaluatedEmployees: 8,
        status: 'APPROVED',
        submittedAt: '2026-10-01T09:00:00Z',
        managerName: 'Trưởng phòng B',
        averageScore: 89.0,
        tierACount: 1,
        tierAPercent: 12.5,
        tierBCount: 4,
        tierCCount: 2,
        tierDCount: 1,
        isQuotaCompliant: true,
        reviewerComment: null,
      },
    ],
  };

  const mockCalibration: CalibrationOverview = {
    department: {
      id: 'd-1',
      code: 'KHCN',
      name: 'Phòng Khách hàng Cá nhân',
      managerName: 'Trưởng phòng A',
    },
    period: {
      id: 'p-1',
      code: 'Q3-2026',
      name: 'Quý 3/2026',
      periodType: 'QUARTER',
      status: 'IN_REVIEW',
      startDate: '2026-07-01',
      endDate: '2099-12-31',
    },
    quota: {
      totalEmployees: 12,
      maxTierAQuota: 1,
      proposedTierACount: 1,
      calibratedTierACount: 1,
      calibratedTierAPercent: 8.3,
      calibratedTierBCount: 6,
      calibratedTierBPercent: 50,
      calibratedTierCCount: 4,
      calibratedTierCPercent: 33.3,
      calibratedTierDCount: 1,
      calibratedTierDPercent: 8.3,
      isCompliant: true,
      complianceNote: 'Tuân thủ phân bổ',
    },
    rankedRoster: [
      {
        employeeId: 'emp-1',
        employeeCode: 'NV001',
        fullName: 'Nguyễn Văn A',
        jobPositionId: 'pos-teller',
        jobPositionTitle: 'Teller',
        departmentId: 'd-1',
        departmentName: 'Phòng Khách hàng Cá nhân',
        businessScore: 90,
        serviceQualityScore: 95,
        operationsScore: 92,
        qualitativeScore: null,
        totalScore: 92.5,
        rank: 1,
        proposedTier: 'A',
        calibratedTier: 'A',
        isAdjusted: false,
        adjustmentReason: null,
        coachingFlag: false,
        disciplinaryFlag: null,
        evaluationStatus: 'IN_REVIEW',
        evaluationId: 'evl-1',
        calculatedAt: '2026-10-01T00:00:00Z',
      },
      {
        employeeId: 'emp-2',
        employeeCode: 'NV002',
        fullName: 'Trần Thị B',
        jobPositionId: 'pos-sales',
        jobPositionTitle: 'Sales Officer',
        departmentId: 'd-1',
        departmentName: 'Phòng Khách hàng Cá nhân',
        businessScore: 85,
        serviceQualityScore: 82,
        operationsScore: 85,
        qualitativeScore: null,
        totalScore: 84.0,
        rank: 2,
        proposedTier: 'B',
        calibratedTier: 'A',
        isAdjusted: true,
        adjustmentReason: 'Hoàn thành vượt bậc chỉ tiêu huy động vốn',
        coachingFlag: false,
        disciplinaryFlag: null,
        evaluationStatus: 'IN_REVIEW',
        evaluationId: 'evl-2',
        calculatedAt: '2026-10-01T00:00:00Z',
      },
    ],
    canSubmit: false,
    overallStatus: 'IN_REVIEW',
    disputedPendingCount: 1,
  };

  beforeEach(async () => {
    evaluationServiceSpy = jasmine.createSpyObj('EvaluationService', [
      'getBranchApprovals',
      'getCalibration',
      'approveDepartment',
      'rejectDepartment',
      'saveBranchAdjustments',
      'finalize',
      'periods',
      'getDepartmentSlotQuotas',
      'updateDepartmentSlotQuotas',
      'getBranchPeriodQuota',
      'updateBranchPeriodQuota',
      'getPeriodFocusKpis',
      'updatePeriodFocusKpis',
    ]);

    i18nServiceSpy = jasmine.createSpyObj('I18nService', ['formatNumber'], {
      localization: {
        evaluations: {
          departmentRanking: {
            colId: 'MÃ NV',
            colName: 'HỌ VÀ TÊN',
            colStatus: 'TRẠNG THÁI',
            colPenalty: 'ĐIỂM PHẠT',
            periodTypeAria: 'Loại kỳ',
            periodAria: 'Kỳ',
            comparePeriodAria: 'So sánh kỳ',
            periodTypeMonth: 'Kỳ tháng',
            periodTypeQuarter: 'Kỳ quý',
            periodTypeYear: 'Kỳ năm',
            comparePeriodNone: 'Không so sánh',
            comparePeriodWith: 'So với: {period}',
          },
          evaluationForm: {
            statusDraft: 'Bản nháp',
            statusCalculated: 'Đã tính',
            statusSubmitted: 'Đã gửi',
            statusDisputed: 'Khiếu nại',
            statusFinal: 'Hoàn thành',
          },
          departmentApproval: {
            pageTitle: 'Phê duyệt xếp hạng chi nhánh',
            breadcrumbHome: 'Trang chủ',
            breadcrumbPeriods: 'Kỳ đánh giá',
            departmentsTitle: 'PHÒNG',
            statusApproved: 'Đã duyệt',
            statusPending: 'Chờ duyệt',
            statusSubmittedAt: 'gửi',
            statusNotSubmitted: 'Chưa gửi',
            btnPublishResults: 'Publish kết quả',
            publishHint: 'Mở khi {approved}/{total} phòng đã duyệt và kiểm tra đạt.',
            btnReject: 'Trả về',
            btnApprove: 'Duyệt',
            rejectModalTitle: 'Yêu cầu hiệu chuẩn lại phòng ban',
            rejectReasonLabel: 'Ý kiến chỉ đạo / Lý do trả về',
            rejectReasonPlaceholder: 'Nhập ý kiến chỉ đạo cụ thể để Trưởng phòng điều chỉnh...',
            rejectReasonMinLength: 'Lý do trả về phải có ít nhất 10 ký tự.',
            rejectModalCancel: 'Hủy',
            rejectModalConfirm: 'Xác nhận trả về',
            approveModalTitle: 'Phê duyệt xếp hạng phòng ban',
            approveModalMessage:
              'Bạn có chắc chắn muốn phê duyệt kết quả xếp hạng của phòng ban này không?',
            approveModalNoteLabel: 'Ghi chú phê duyệt (không bắt buộc)',
            approveModalNotePlaceholder: 'Nhập ghi chú phê duyệt...',
            approveModalCancel: 'Hủy',
            approveModalConfirm: 'Xác nhận duyệt',
            approveSuccess: 'Đã phê duyệt kết quả xếp hạng phòng ban thành công.',
            rejectSuccess: 'Đã trả hồ sơ về Trưởng phòng để hiệu chuẩn lại.',
            tabHiddenSuffix: 'ẩn',
            colIndex: '#',
            colEmployee: 'NHÂN VIÊN',
            colTotalPe: 'ĐIỂM PE',
            colProposed: 'ĐỀ XUẤT',
            colFinalized: 'TP CHỐT',
            colManagerReason: 'LÝ DO TRƯỞNG PHÒNG',
            quotaRatioPrefix: 'So với phân bổ tier:',
            quotaMatched: 'Khớp',
            footnote:
              'Giám đốc chi nhánh chỉ Duyệt hoặc Trả về, không sửa tier trực tiếp. Bấm tên nhân viên → SCR-PRF-05 (truy vết điểm).',
            loading: 'Đang tải dữ liệu xếp hạng...',
          },
          branchPeriods: {
            modalTitle: 'Khóa và công bố kỳ đánh giá',
            lockConfirm:
              'Hành động này sẽ khóa toàn bộ kết quả đánh giá của chi nhánh và công bố tới nhân viên. Không thể hoàn tác sau khi xác nhận.',
            signatureToken: 'Mã PIN / Token xác thực phê duyệt',
            signaturePlaceholder: 'Nhập mã PIN xác thực...',
            approvalComments: 'Ý kiến phê duyệt của Giám đốc',
            commentsPlaceholder: 'Nhập ý kiến phê duyệt...',
            cancel: 'Hủy',
            confirmFinalize: 'Xác nhận khóa và công bố',
            lockSuccess: 'Khóa kỳ đánh giá thành công.',
          },
        },
      } as unknown as Localization,
    });

    evaluationServiceSpy.getBranchApprovals.and.returnValue(of(mockOverview));
    evaluationServiceSpy.getCalibration.and.returnValue(of(mockCalibration));

    evaluationServiceSpy.periods.and.returnValue(
      of([
        {
          id: 'p-1',
          code: 'Q3-2026',
          name: 'Quý 3/2026',
          startDate: '2026-07-01',
          endDate: '2099-12-31',
          periodType: 'QUARTER',
          status: 'IN_REVIEW',
          isActive: true,
        },
      ]),
    );
    evaluationServiceSpy.getDepartmentSlotQuotas.and.returnValue(
      of({
        departmentId: 'd-1',
        slots: [
          { tierCode: 'A1', headcount: 1 },
          { tierCode: 'A2', headcount: 2 },
          { tierCode: 'A3', headcount: 3 },
          { tierCode: 'B', headcount: 4 },
          { tierCode: 'C', headcount: 5 },
        ],
      }),
    );
    evaluationServiceSpy.getBranchPeriodQuota.and.returnValue(
      of({
        departmentId: 'b-1',
        periodId: 'p-1',
        slots: [
          { tierCode: 'A1', headcount: 2 },
          { tierCode: 'A2', headcount: 3 },
          { tierCode: 'A3', headcount: 4 },
          { tierCode: 'B', headcount: 8 },
          { tierCode: 'C', headcount: 3 },
        ],
      }),
    );
    evaluationServiceSpy.updateBranchPeriodQuota.and.returnValue(
      of({
        departmentId: 'b-1',
        periodId: 'p-1',
        slots: [
          { tierCode: 'A1', headcount: 5 },
          { tierCode: 'A2', headcount: 5 },
          { tierCode: 'A3', headcount: 5 },
          { tierCode: 'B', headcount: 5 },
          { tierCode: 'C', headcount: 5 },
        ],
      }),
    );
    evaluationServiceSpy.getPeriodFocusKpis.and.returnValue(
      of({ periodId: 'p-1', focusKpis: [] }),
    );
    evaluationServiceSpy.updatePeriodFocusKpis.and.returnValue(
      of({ periodId: 'p-1', focusKpis: [] }),
    );
    i18nServiceSpy.formatNumber.and.callFake((val: number) => String(val));

    const kpiDefinitionServiceSpy = jasmine.createSpyObj('KpiDefinitionService', ['list']);
    kpiDefinitionServiceSpy.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 100 }));

    await TestBed.configureTestingModule({
      imports: [DepartmentRankingApprovalComponent],
      providers: [
        provideRouter([]),
        { provide: EvaluationService, useValue: evaluationServiceSpy },
        { provide: KpiDefinitionService, useValue: kpiDefinitionServiceSpy },
        { provide: I18nService, useValue: i18nServiceSpy },
        ...provideRouteState({ periodId: 'p-1' }).providers,
      ],
    })
      .overrideComponent(DepartmentRankingApprovalComponent, {
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

    fixture = TestBed.createComponent(DepartmentRankingApprovalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and load branch overview', () => {
    expect(component).toBeTruthy();
    expect(evaluationServiceSpy.getBranchApprovals).toHaveBeenCalledWith('p-1');
    expect(component.branchOverview()).toEqual(mockOverview);
    expect(component.selectedDepartmentId()).toBe('d-1');
  });

  it('should auto-load calibration for first department', () => {
    expect(evaluationServiceSpy.getCalibration).toHaveBeenCalledWith('p-1', 'd-1', undefined, true);
    expect(component.calibrationData()).toEqual(mockCalibration);
    expect(component.positionTabs().length).toBe(3);
  });

  it('should filter roster by selected position tab', () => {
    component.selectPosition('pos-teller');
    fixture.detectChanges();
    expect(component.filteredRoster().length).toBe(1);
    expect(component.filteredRoster()[0].employeeCode).toBe('NV001');

    component.selectPosition('pos-sales');
    fixture.detectChanges();
    expect(component.filteredRoster().length).toBe(1);
    expect(component.filteredRoster()[0].employeeCode).toBe('NV002');
  });

  it('should show TP CHOT and reason columns only when department is submitted', () => {
    component.setViewMode('single');
    // d-1 is IN_REVIEW (submitted)
    expect(component.isDepartmentSubmitted()).toBeTrue();
    fixture.detectChanges();
    const headers = fixture.nativeElement.querySelectorAll(
      '.ranking-table th',
    ) as NodeListOf<HTMLTableCellElement>;
    const headerTexts = Array.from(headers).map((h) => h.textContent?.trim());
    const finalizedLabel =
      component.localization.evaluations.departmentApproval.colFinalized || 'TP CHỐT';
    const reasonLabel =
      component.localization.evaluations.departmentApproval.colManagerReason ||
      'LÝ DO TRƯỞNG PHÒNG';
    expect(headerTexts).toContain(finalizedLabel);
    expect(headerTexts).toContain(reasonLabel);

    // Switch to a department that is not submitted
    const unsubmittedDept = {
      ...mockOverview.departments[0],
      departmentId: 'd-unsubmitted',
      status: 'NOT_SUBMITTED' as const,
    };
    component.branchOverview.set({
      ...mockOverview,
      departments: [unsubmittedDept],
    });
    component.selectedDepartmentId.set('d-unsubmitted');
    fixture.detectChanges();

    expect(component.isDepartmentSubmitted()).toBeFalse();
    const updatedHeaders = fixture.nativeElement.querySelectorAll(
      '.ranking-table th',
    ) as NodeListOf<HTMLTableCellElement>;
    const updatedHeaderTexts = Array.from(updatedHeaders).map((h) => h.textContent?.trim());
    expect(updatedHeaderTexts).not.toContain(finalizedLabel);
    expect(updatedHeaderTexts).not.toContain(reasonLabel);
  });

  it('should open and submit approve modal successfully', () => {
    const approveResult: DepartmentApprovalResult = {
      departmentId: 'd-1',
      periodId: 'p-1',
      status: 'APPROVED',
      approvedEvaluationsCount: 12,
      message: 'Phê duyệt thành công',
    };
    evaluationServiceSpy.approveDepartment.and.returnValue(of(approveResult));

    component.openApproveModal();
    expect(component.isApproveModalOpen()).toBeTrue();

    component.approveNote.set('Đồng ý xếp hạng');
    component.submitApproveDepartment();

    expect(evaluationServiceSpy.approveDepartment).toHaveBeenCalledWith('d-1', {
      periodId: 'p-1',
      note: 'Đồng ý xếp hạng',
    });
    expect(component.isApproveModalOpen()).toBeFalse();
    expect(component.toastMessage()).toContain('thành công');
  });

  it('should validate reject reason minlength and submit reject successfully', () => {
    const rejectResult: CalibrationDecisionResult = {
      departmentId: 'd-1',
      periodId: 'p-1',
      status: 'RECALIBRATION_REQUESTED',
      message: 'Đã trả về',
    };
    evaluationServiceSpy.rejectDepartment.and.returnValue(of(rejectResult));

    component.openRejectModal();
    expect(component.isRejectModalOpen()).toBeTrue();

    // Less than 10 chars
    component.rejectReason.set('Ngắn');
    component.submitRejectDepartment();
    expect(component.rejectError()).toBeTruthy();
    expect(evaluationServiceSpy.rejectDepartment).not.toHaveBeenCalled();

    // Valid >= 10 chars
    component.rejectReason.set('Cần điều chỉnh lại tỷ lệ A do vượt trần.');
    component.submitRejectDepartment();

    expect(evaluationServiceSpy.rejectDepartment).toHaveBeenCalledWith('d-1', {
      periodId: 'p-1',
      reason: 'Cần điều chỉnh lại tỷ lệ A do vượt trần.',
    });
    expect(component.isRejectModalOpen()).toBeFalse();
  });

  it('should submit lock period successfully', () => {
    const lockResult: FinalizePeriodResult = {
      periodId: 'p-1',
      periodStatus: 'LOCKED',
      finalizedAt: '2026-10-02T10:00:00Z',
      totalEvaluationsFinalized: 20,
      message: 'Khóa thành công',
    };
    evaluationServiceSpy.finalize.and.returnValue(of(lockResult));

    component.openLockModal();
    component.lockSignatureToken.set('123456');
    component.lockApprovalComments.set('Đồng ý khóa kết quả');
    component.submitLockPeriod();

    expect(evaluationServiceSpy.finalize).toHaveBeenCalledWith({
      periodId: 'p-1',
      signatureToken: '123456',
      approvalComments: 'Đồng ý khóa kết quả',
    });
    expect(component.isLockModalOpen()).toBeFalse();
  });

  it('should navigate to evaluations/form when clicking employee link', () => {
    component.navigateToTraceability('emp-1');
    expect(router.navigate).toHaveBeenCalledWith(['/evaluations/form']);
  });

  it('should update department quota when BM edits and submits', () => {
    evaluationServiceSpy.updateDepartmentSlotQuotas.and.returnValue(
      of({
        departmentId: 'd-1',
        slots: [
          { tierCode: 'A1', headcount: 2 },
          { tierCode: 'A2', headcount: 2 },
          { tierCode: 'A3', headcount: 3 },
          { tierCode: 'B', headcount: 4 },
          { tierCode: 'C', headcount: 5 },
        ],
      }),
    );

    component.departmentQuotas.set([
      { tierCode: 'A1', headcount: 1 },
      { tierCode: 'A2', headcount: 2 },
      { tierCode: 'A3', headcount: 2 },
      { tierCode: 'B', headcount: 5 },
      { tierCode: 'C', headcount: 2 },
    ]);
    component.onQuotaHeadcountChange({ tier: 'A1', headcount: 2 });
    component.onQuotaHeadcountChange({ tier: 'B', headcount: 4 });
    component.submitDepartmentQuota();

    expect(evaluationServiceSpy.updateDepartmentSlotQuotas).toHaveBeenCalled();
    expect(component.toastMessage()).toContain('thành công');
  });

  it('should not allow updating quota when period is locked or closed', () => {
    evaluationServiceSpy.updateDepartmentSlotQuotas.calls.reset();
    component.periods.set([
      {
        id: 'p-1',
        code: 'PER-LOCKED',
        name: 'Kỳ đã chốt',
        startDate: '2026-09-01',
        endDate: '2026-09-30',
        periodType: 'MONTH',
        status: 'LOCKED',
        isActive: false,
      },
    ]);
    component.periodId.set('p-1');

    expect(component.isPeriodLocked()).toBeTrue();

    const oldHeadcount = component.departmentQuotas().find((s) => s.tierCode === 'A1')?.headcount;
    component.onQuotaHeadcountChange({ tier: 'A1', headcount: 99 });
    expect(component.departmentQuotas().find((s) => s.tierCode === 'A1')?.headcount).toBe(
      oldHeadcount,
    );

    component.submitDepartmentQuota();
    expect(evaluationServiceSpy.updateDepartmentSlotQuotas).not.toHaveBeenCalled();
  });

  it('should switch viewMode between single and all', () => {
    expect(component.viewMode()).toBe('all');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Phân bố chỉ tiêu phòng');

    component.setViewMode('single');
    expect(component.viewMode()).toBe('single');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Phân bố chỉ tiêu phòng');

    component.setViewMode('all');
    expect(component.viewMode()).toBe('all');
  });

  it('should toggle department expansion and load calibration in all view', () => {
    expect(component.isDepartmentExpanded('d-1')).toBeFalse();
    component.toggleExpandDepartment('d-1');
    expect(component.isDepartmentExpanded('d-1')).toBeTrue();
    expect(evaluationServiceSpy.getCalibration).toHaveBeenCalledWith('p-1', 'd-1', undefined, true);

    component.toggleExpandDepartment('d-1');
    expect(component.isDepartmentExpanded('d-1')).toBeFalse();
  });

  it('should manage and save branch period total quota', () => {
    expect(component.isEditingPeriodQuota()).toBeFalse();
    component.startEditPeriodQuota();
    expect(component.isEditingPeriodQuota()).toBeTrue();

    component.onPeriodQuotaInputChange('A1', '1');
    component.onPeriodQuotaInputChange('A2', '0');
    component.onPeriodQuotaInputChange('A3', '0');
    component.onPeriodQuotaInputChange('B', '6');
    component.onPeriodQuotaInputChange('C', '1');
    expect(component.periodQuotaDraft()['A1']).toBe(1);

    component.savePeriodQuota();
    expect(evaluationServiceSpy.updateBranchPeriodQuota).toHaveBeenCalled();
    expect(component.isEditingPeriodQuota()).toBeFalse();
    expect(component.toastMessage()).toContain('thành công');
  });

  it('should not allow starting edit of period quota when period is locked', () => {
    component.periods.set([
      {
        id: 'p-1',
        code: 'PER-LOCKED',
        name: 'Kỳ đã chốt',
        startDate: '2026-09-01',
        endDate: '2026-09-30',
        periodType: 'MONTH',
        status: 'LOCKED',
        isActive: false,
      },
    ]);
    component.startEditPeriodQuota();
    expect(component.isEditingPeriodQuota()).toBeFalse();
  });

  it('should consider past period with status OPEN as locked', () => {
    component.periods.set([
      {
        id: 'p-past',
        code: 'PER-PAST',
        name: 'Tháng 10/2026',
        startDate: '2026-10-01',
        endDate: '2026-10-31',
        periodType: 'MONTH',
        status: 'OPEN',
        isActive: true,
      },
    ]);
    component.periodId.set('p-past');
    expect(component.isPeriodLocked()).toBeTrue();
  });

  it('should calculate totalBranchTierASlots in real time during draft edit and allow saving', () => {
    component.branchOverview.set({
      ...mockOverview,
      period: {
        ...mockOverview.period,
        id: 'p-1',
        name: 'Kỳ test',
        periodType: 'MONTH',
        status: 'OPEN',
        startDate: '2099-01-01',
        endDate: '2099-01-31',
      },
      departments: [],
      canFinalize: false,
      totalEmployees: 5,
    });
    component.periods.set([
      {
        id: 'p-1',
        code: 'PER-1',
        name: 'Kỳ test',
        startDate: '2099-01-01',
        endDate: '2099-01-31',
        periodType: 'MONTH',
        status: 'OPEN',
        isActive: true,
      },
    ]);
    component.periodId.set('p-1');
    component.periodQuota.set([
      { tierCode: 'A1', headcount: 1 },
      { tierCode: 'A2', headcount: 1 },
      { tierCode: 'A3', headcount: 1 },
      { tierCode: 'B', headcount: 1 },
      { tierCode: 'C', headcount: 1 },
    ]);

    expect(component.totalBranchTierASlots()).toBe(3);

    // Start editing
    component.startEditPeriodQuota();
    expect(component.isEditingPeriodQuota()).toBeTrue();

    // Type headcount for A1 = 5 (total Tier A = 7, exceeds 20% of 20 = 4)
    component.onPeriodQuotaInputChange('A1', '5');
    expect(component.totalBranchTierASlots()).toBe(7);
    expect(component.periodQuotaRatioError()).toBeTruthy();

    component.savePeriodQuota();
    expect(evaluationServiceSpy.updateBranchPeriodQuota).not.toHaveBeenCalled();

    // Adjust A1 = 0, A2 = 0, A3 = 1 (total Tier A = 1 <= 1) -> should save
    component.onPeriodQuotaInputChange('A1', '0');
    component.onPeriodQuotaInputChange('A2', '0');
    expect(component.periodQuotaRatioError()).toBeNull();
    evaluationServiceSpy.updateBranchPeriodQuota.and.returnValue(
      of({
        departmentId: '00000000-0000-0000-0000-000000000000',
        periodId: 'p-1',
        slots: [
          { tierCode: 'A1', headcount: 0 },
          { tierCode: 'A2', headcount: 0 },
          { tierCode: 'A3', headcount: 1 },
          { tierCode: 'B', headcount: 1 },
          { tierCode: 'C', headcount: 1 },
        ],
      }),
    );
    component.savePeriodQuota();
    expect(evaluationServiceSpy.updateBranchPeriodQuota).toHaveBeenCalled();
  });

  it('should validate department slot quota against period quota max and department headcount', () => {
    component.periodQuota.set([
      { tierCode: 'A1', headcount: 1 },
      { tierCode: 'A2', headcount: 2 },
      { tierCode: 'A3', headcount: 2 },
      { tierCode: 'B', headcount: 5 },
      { tierCode: 'C', headcount: 5 },
    ]);
    component.selectedDepartmentId.set('d-1');
    component.branchOverview.set({
      ...mockOverview,
      period: {
        ...mockOverview.period,
        id: 'p-1',
        name: 'Kỳ test',
        periodType: 'MONTH',
        status: 'OPEN',
        startDate: '2099-01-01',
        endDate: '2099-01-31',
      },
      departments: [
        {
          ...mockOverview.departments[0],
          departmentId: 'd-1',
          departmentCode: 'KD',
          departmentName: 'Kinh doanh',
          totalEmployees: 2,
          status: 'NOT_SUBMITTED',
          managerName: 'TP',
        },
      ],
      canFinalize: false,
      totalEmployees: 15,
    });

    // Set department quota A1 = 2 (exceeds branch A1=1)
    component.departmentQuotas.set([
      { tierCode: 'A1', headcount: 2 },
      { tierCode: 'A2', headcount: 0 },
      { tierCode: 'A3', headcount: 0 },
      { tierCode: 'B', headcount: 0 },
      { tierCode: 'C', headcount: 0 },
    ]);

    expect(component.isDeptQuotaExceeded()).toBeTrue();
    expect(component.deptQuotaTierExceededErrors().length).toBeGreaterThan(0);
    expect(component.deptQuotaTierExceededErrors()[0]).toContain('vượt trần Quota tổng');

    // Submit should be blocked
    component.submitDepartmentQuota();
    expect(evaluationServiceSpy.updateDepartmentSlotQuotas).not.toHaveBeenCalled();
  });

  it('should allow BM to adjust submitted rankings, save on Submitted status, show Message warnings, and finalize via Duyệt popover', () => {
    evaluationServiceSpy.saveBranchAdjustments.and.returnValue(
      of({
        departmentId: 'd-1',
        periodId: 'p-1',
        status: 'Submitted',
        adjustedCount: 1,
        message: 'OK',
      }),
    );
    evaluationServiceSpy.finalize.and.returnValue(
      of({
        periodId: 'p-1',
        periodCode: 'Q3-2026',
        periodStatus: 'LOCKED',
        finalizedAt: '2026-11-27T08:00:00Z',
        totalEvaluationsFinalized: 20,
        message: 'Locked',
      }),
    );

    // Message warnings present before finalizing (missing period quota, disputedPendingCount = 1)
    expect(component.branchFinalizeWarnings().length).toBeGreaterThan(0);

    // Start adjusting
    component.startBranchAdjusting();
    expect(component.isAdjustingBranch()).toBeTrue();
    expect(component.isDepartmentExpanded('d-1')).toBeTrue();

    // Change tier & reason for emp-1 in mockCalibration
    component.onBranchEmpTierChange('emp-1', 'B');
    component.onBranchEmpReasonChange('emp-1', 'Điều chỉnh theo chi nhánh');
    component.saveBranchDraft();

    expect(evaluationServiceSpy.saveBranchAdjustments).toHaveBeenCalledWith(
      jasmine.objectContaining({
        departmentId: 'd-1',
        periodId: 'p-1',
        isDraft: false,
      }),
    );
    expect(component.isAdjustingBranch()).toBeFalse();

    // Open anchored Duyệt popover and confirm final approval
    component.openApprovePopover();
    expect(component.approvePopoverOpen()).toBeTrue();

    evaluationServiceSpy.periods.and.returnValue(
      of([
        {
          id: 'p-1',
          code: 'Q3-2026',
          name: 'Quý 3/2026',
          startDate: '2026-07-01',
          endDate: '2099-12-31',
          periodType: 'QUARTER',
          status: 'LOCKED',
          isActive: false,
        },
      ]),
    );
    component.confirmFinalApprove();

    expect(evaluationServiceSpy.finalize).toHaveBeenCalledWith({ periodId: 'p-1' });
    expect(component.isBranchFinalized()).toBeTrue();
  });
});
