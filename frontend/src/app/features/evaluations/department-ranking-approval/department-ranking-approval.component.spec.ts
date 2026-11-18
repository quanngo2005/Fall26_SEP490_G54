import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { DepartmentRankingApprovalComponent } from './department-ranking-approval.component';
import { EvaluationService } from '../../../core/evaluation.service';
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
      endDate: '2026-09-30',
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
      endDate: '2026-09-30',
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
      'finalize',
      'getPreCloseCheck',
    ]);

    i18nServiceSpy = jasmine.createSpyObj('I18nService', [], {
      localization: {
        evaluations: {
          departmentApproval: {
            pageTitle: 'Phê duyệt xếp hạng chi nhánh',
            breadcrumbHome: 'Trang chủ',
            breadcrumbPeriods: 'Kỳ đánh giá',
            departmentsTitle: 'PHÒNG',
            statusApproved: 'Đã duyệt',
            statusPending: 'Chờ duyệt',
            statusSubmittedAt: 'gửi',
            statusNotSubmitted: 'Chưa gửi',
            btnPreCloseCheck: 'Kiểm tra trước khi đóng',
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
            colTotalPe: 'TỔNG PE',
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
    evaluationServiceSpy.getPreCloseCheck.and.returnValue(
      of({
        periodId: 'p-1',
        periodCode: '2026-Q3',
        periodName: 'Quý 3/2026',
        startDate: '2026-07-01',
        endDate: '2026-09-30',
        preCloseDeadline: '2026-10-12T17:00:00Z',
        canPublish: false,
        totalUnresolvedCount: 0,
        items: [],
      }),
    );

    await TestBed.configureTestingModule({
      imports: [DepartmentRankingApprovalComponent],
      providers: [
        provideRouter([]),
        { provide: EvaluationService, useValue: evaluationServiceSpy },
        { provide: I18nService, useValue: i18nServiceSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(convertToParamMap({ periodId: 'p-1' })),
          },
        },
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
    expect(evaluationServiceSpy.getCalibration).toHaveBeenCalledWith('p-1', 'd-1');
    expect(component.calibrationData()).toEqual(mockCalibration);
    expect(component.positionTabs().length).toBe(2);
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

  it('should open pre-close modal and continue to lock modal', () => {
    component.openPreCloseModal();
    expect(component.isPreCloseModalOpen()).toBeTrue();

    component.onPreCloseContinuePublish();
    expect(component.isPreCloseModalOpen()).toBeFalse();
    expect(component.isLockModalOpen()).toBeTrue();
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

  it('should navigate to traceability when clicking employee link', () => {
    component.navigateToTraceability('emp-1');
    expect(router.navigate).toHaveBeenCalledWith(['/performance-traceability'], {
      queryParams: { employeeId: 'emp-1', periodId: 'p-1' },
    });
  });
});
