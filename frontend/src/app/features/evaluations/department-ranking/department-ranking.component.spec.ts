import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { DepartmentRankingComponent } from './department-ranking.component';
import { EvaluationService } from '../../../core/evaluation.service';
import { I18nService } from '../../../core/i18n/i18n.service';
import {
  EvaluationPeriodOption,
  RankingBoardOverview,
  RankingBoardStaffRow,
} from '../../../core/evaluation.models';
import { HeaderComponent, SidebarComponent } from '../../../shared/layout';
import { LayoutIconComponent } from '../../../shared/layout/layout-icon.component';

@Component({ selector: 'app-header', standalone: true, template: '' })
class StubHeaderComponent {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class StubSidebarComponent {}

@Component({ selector: 'app-layout-icon', standalone: true, template: '' })
class StubIconComponent {
  readonly name = input<string>();
}

describe('DepartmentRankingComponent', () => {
  let component: DepartmentRankingComponent;
  let fixture: ComponentFixture<DepartmentRankingComponent>;
  let evaluationServiceSpy: jasmine.SpyObj<EvaluationService>;
  let router: Router;

  const period: EvaluationPeriodOption = {
    id: 'p-1',
    code: 'Q3-2026',
    name: 'Quý 3/2026',
    periodType: 'QUARTER',
    status: 'OPEN',
    startDate: '2026-07-01',
    endDate: '2026-09-30',
    isActive: true,
  };

  const row: RankingBoardStaffRow = {
    rank: 1,
    employeeId: 'emp-1',
    employeeCode: 'NV001',
    fullName: 'Nguyễn Văn A',
    onTimeSla: { value: 95, previousValue: 90, change: 5 },
    onTimeCompletedCount: 19,
    completedWithDeadlineCount: 20,
    achievedCriteria: 3,
    totalCriteria: 4,
    criteria: [
      { kpiName: 'Doanh số', actualValue: 120, targetValue: 100, unit: '%', isAchieved: true },
    ],
    riskEventCount: { value: 1, previousValue: 0, change: 1 },
    riskEvents: [{ code: 'R-01', description: 'Trễ hạn', severity: 'LOW' }],
    riskPenalty: { value: 0.5, previousValue: 0, change: 0.5 },
    quantitativeScore: { value: 92.5, previousValue: 90, change: 2.5 },
    systemTier: 'A1',
    finalTier: 'A1',
    adjustmentReason: null,
    evaluationStatus: 'CALCULATED',
  };

  const board: RankingBoardOverview = {
    departmentId: 'd-1',
    departmentName: 'Phòng Khách hàng Cá nhân',
    periodId: 'p-1',
    periodCode: 'Q3-2026',
    periodName: 'Quý 3/2026',
    comparePeriodId: null,
    comparePeriodCode: null,
    rankingLockDay: 5,
    rankingLockDate: '2026-10-05',
    isLocked: false,
    canSendToDirector: false,
    isSentToDirector: false,
    sendBlockers: ['Còn nhân viên chưa được tính điểm'],
    tiers: [
      { tierCode: 'A1', headcount: 1, actualCount: 1, isFilled: true },
      { tierCode: 'A2', headcount: 0, actualCount: 0, isFilled: true },
      { tierCode: 'A3', headcount: 0, actualCount: 0, isFilled: true },
      { tierCode: 'B', headcount: 0, actualCount: 0, isFilled: true },
    ],
    staffRows: [row],
    generatedAt: '2026-10-01T00:00:00Z',
    isTeam: false,
  };

  beforeEach(async () => {
    evaluationServiceSpy = jasmine.createSpyObj('EvaluationService', [
      'periods',
      'rankingBoard',
      'saveRankingBoardDraft',
      'autoAssignRankingBoard',
      'sendRankingBoardToDirector',
      'exportRankingBoard',
    ]);
    evaluationServiceSpy.periods.and.returnValue(of([period]));
    evaluationServiceSpy.rankingBoard.and.returnValue(of(board));

    await TestBed.configureTestingModule({
      imports: [DepartmentRankingComponent],
      providers: [
        provideRouter([]),
        { provide: EvaluationService, useValue: evaluationServiceSpy },
        I18nService,
      ],
    })
      .overrideComponent(DepartmentRankingComponent, {
        remove: {
          imports: [HeaderComponent, SidebarComponent, LayoutIconComponent],
        },
        add: {
          imports: [StubHeaderComponent, StubSidebarComponent, StubIconComponent],
        },
      })
      .compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);

    fixture = TestBed.createComponent(DepartmentRankingComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and load the ranking board of the first period', () => {
    expect(component).toBeTruthy();
    expect(evaluationServiceSpy.periods).toHaveBeenCalled();
    expect(evaluationServiceSpy.rankingBoard).toHaveBeenCalledWith('p-1', null, null);
    expect(component.board()).toEqual(board);
    expect(component.selectedPeriodId()).toBe('p-1');
  });

  it('should show the staff row and tier table once the board is loaded', () => {
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Nguyễn Văn A');
    expect(component.tierTableRows().length).toBe(4);
    expect(component.totalSlots()).toBe(1);
    expect(component.totalActual()).toBe(1);
  });

  it('should keep sending to director enabled even while the board has blockers', () => {
    expect(component.canSend()).toBeFalse();
    const sendButton = findButton(fixture, 'Gửi giám đốc chi nhánh');
    expect(sendButton?.disabled).toBeFalse();
  });

  it('should enable sending to director when the board allows it', () => {
    component.board.set({ ...board, canSendToDirector: true, sendBlockers: [] });
    fixture.detectChanges();
    expect(component.canSend()).toBeTrue();
  });

  it('should hide send to director button and show save proposal when isTeam is true', () => {
    component.board.set({ ...board, isTeam: true });
    fixture.detectChanges();
    expect(component.isTeam()).toBeTrue();
    const sendButton = findButton(fixture, 'Gửi giám đốc chi nhánh');
    expect(sendButton).toBeUndefined();
    const saveButton = findButton(fixture, 'Lưu đề xuất xếp loại');
    expect(saveButton).toBeDefined();
  });

  it('should open the send confirmation even when canSendToDirector is false', () => {
    component.openConfirmSend();
    expect(component.confirmSendOpen()).toBeTrue();
  });

  it('should keep adjusting disabled until the ranking lock day is reached', () => {
    expect(component.lockReached()).toBeFalse();
    expect(component.canAdjustRow(row)).toBeTrue();
    expect(component.canAdjustRow({ ...row, evaluationStatus: 'DRAFT' })).toBeTrue();
    expect(component.canAdjustRow({ ...row, evaluationStatus: 'FINAL' })).toBeFalse();
  });

  it('should save the draft with the current tier adjustments', () => {
    evaluationServiceSpy.saveRankingBoardDraft.and.returnValue(of(board));
    component.saveDraft();
    expect(evaluationServiceSpy.saveRankingBoardDraft).toHaveBeenCalledWith({
      departmentId: 'd-1',
      periodId: 'p-1',
      adjustments: [{ employeeId: 'emp-1', finalTier: 'A1', reason: null }],
    });
    expect(component.toastType()).toBe('success');
  });

  it('should show an error toast when saving the draft fails', () => {
    evaluationServiceSpy.saveRankingBoardDraft.and.returnValue(throwError(() => ({ status: 500 })));
    component.saveDraft();
    expect(component.toastType()).toBe('error');
    expect(component.toastMessage()).toBeTruthy();
  });

  it('should require a reason when a tier is changed', () => {
    component.setFinalTier(row, 'B');
    expect(component.isTierChanged(row)).toBeTrue();
    expect(component.isReasonMissing(row)).toBeTrue();

    component.setReason(row, 'Hoàn thành vượt chỉ tiêu');
    expect(component.isReasonMissing(row)).toBeFalse();
  });

  it('should navigate to the evaluation form of the selected employee', () => {
    component.openEmployeeForm(row);
    expect(router.navigate).toHaveBeenCalledWith(['/evaluations/form']);
  });

  it('should sort staff by composite criteria when a composite sort is chosen', () => {
    component.onCompositeSortSelected('criteria');
    expect(component.compositeSort()).toBe('criteria');
    expect(component.displayRows().length).toBe(1);
  });

  it('should filter staff rows by evaluation status', () => {
    component.onStatusFilterSelected('DRAFT');
    expect(component.displayRows().length).toBe(0);
    component.onStatusFilterSelected('CALCULATED');
    expect(component.displayRows().length).toBe(1);
    component.onStatusFilterSelected('all');
    expect(component.displayRows().length).toBe(1);
  });
});

function findButton(
  fixture: ComponentFixture<DepartmentRankingComponent>,
  label: string,
): HTMLButtonElement | undefined {
  const buttons = Array.from(
    fixture.nativeElement.querySelectorAll('button'),
  ) as HTMLButtonElement[];
  return buttons.find((button) => button.textContent?.trim() === label);
}
