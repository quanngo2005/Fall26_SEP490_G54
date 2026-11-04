import { HttpErrorResponse } from '@angular/common/http';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import {
  SuggestedDailyTarget,
  TargetAssignment,
  TargetAssignmentFeedback,
} from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { businessClock } from '../work-board/work-board.utils';
import { MyTargetsComponent } from './my-targets.component';

/** The running month and the next one, so the month marked "in progress" is the same whenever the test runs. */
const THIS_MONTH = businessClock(new Date()).date.slice(0, 7);
const NEXT_MONTH = (() => {
  const [year, month] = THIS_MONTH.split('-').map(Number);
  return month === 12 ? `${year + 1}-01` : `${year}-${String(month + 1).padStart(2, '0')}`;
})();
const MONTH_NUMBER = THIS_MONTH.slice(5);

@Component({ selector: 'app-header', standalone: true, template: '' })
class HeaderStub {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class SidebarStub {}

const ME = { id: 'e1', fullName: 'Trần Quốc Bảo', managerName: 'Phạm Thu Hà' } as EmployeeDetail;
const PERIOD = {
  id: 'q4',
  code: '2026-Q4',
  name: 'Quý 4/2026',
  periodType: 'QUARTER',
  startDate: '2026-10-01',
  endDate: '2026-12-31',
  status: 'OPEN',
  isActive: true,
} as EvaluationPeriodOption;
const KPIS = [
  { id: 'k1', code: 'HDV', name: 'Tiền gửi', unit: 'VND' },
  { id: 'k2', code: 'THE', name: 'Mở thẻ', unit: 'HĐ' },
] as KpiDefinition[];

const target = (
  id: string,
  kpiId: string,
  value: number,
  extra: Partial<TargetAssignment> = {},
): TargetAssignment => ({
  id,
  kpiDefinitionId: kpiId,
  evaluationPeriodId: 'q4',
  departmentId: null,
  employeeId: 'e1',
  parentAssignmentId: null,
  targetValue: value,
  status: 'ACTIVE',
  updatedAt: '2026-10-20T09:15:02+07:00',
  feedbackStatus: 'NOT_VIEWED',
  feedbackAt: null,
  ...extra,
});

const item = (id: string, kpiId: string, months: [string, number, number][]) => ({
  targetAssignmentId: id,
  kpiDefinitionId: kpiId,
  kpiCode: kpiId,
  unit: 'VND',
  isWholeNumber: false,
  targetValue: 1,
  plannedDaily: 1,
  plannedWeekly: 5,
  months: months.map(([month, workingDays, value]) => ({
    month,
    workingDays,
    value,
    daily: 1,
    weekly: 5,
  })),
});

const DAILY: SuggestedDailyTarget = {
  employeeId: 'e1',
  evaluationPeriodId: 'q4',
  periodStart: '2026-10-01',
  periodEnd: '2026-12-31',
  workingDays: 64,
  items: [
    item('t1', 'k1', [
      [THIS_MONTH, 22, 2200],
      [NEXT_MONTH, 21, 2100],
    ]),
    item('t2', 'k2', [
      [THIS_MONTH, 22, 17],
      [NEXT_MONTH, 21, 17],
    ]),
  ],
};

const feedback = (
  type: 'CONFIRM' | 'COMMENT',
  comment: string | null = null,
): TargetAssignmentFeedback => ({
  id: `f-${type}-${comment}`,
  targetAssignmentId: 't1',
  employeeId: 'e1',
  type,
  comment,
  createdAt: '2026-10-21T07:02:11+00:00',
});

describe('MyTargetsComponent', () => {
  let targets: jasmine.SpyObj<TargetAssignmentService>;
  let current: TargetAssignment[];
  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const root = (fixture: ComponentFixture<unknown>) => fixture.nativeElement as HTMLElement;
  const click = (fixture: ComponentFixture<unknown>, selector: string) => {
    root(fixture).querySelector<HTMLElement>(selector)?.click();
    fixture.detectChanges();
  };

  async function create(
    history: TargetAssignmentFeedback[] = [],
  ): Promise<ComponentFixture<MyTargetsComponent>> {
    targets.feedback.and.returnValue(of(history));
    const fixture = TestBed.createComponent(MyTargetsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(async () => {
    // The period is far in the future so the "today" of the test run is always before it ends.
    const period = { ...PERIOD, startDate: '2020-10-01', endDate: '2099-12-31' };
    current = [target('t1', 'k1', 6_400_000_000), target('t2', 'k2', 50)];
    targets = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'mine',
      'confirm',
      'comment',
      'suggestedDaily',
      'feedback',
      'breakdown',
    ]);
    targets.mine.and.callFake(() => of(current));
    targets.suggestedDaily.and.returnValue(of(DAILY));
    await TestBed.configureTestingModule({
      imports: [MyTargetsComponent],
      providers: [
        { provide: TargetAssignmentService, useValue: targets },
        { provide: EmployeeService, useValue: { me: () => of(ME) } },
        { provide: EvaluationService, useValue: { periods: () => of([period]) } },
        {
          provide: KpiDefinitionService,
          useValue: { list: () => of({ items: KPIS, totalCount: 2, page: 1, pageSize: 200 }) },
        },
      ],
    })
      .overrideComponent(MyTargetsComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('shows the quarter with one column per KPI and the month rows of the open quarter', async () => {
    const fixture = await create();

    const heads = Array.from(root(fixture).querySelectorAll('thead th')).map((th) => text(th));
    expect(heads).toEqual(['KỲ', 'NGÀY ĐI LÀM', 'Tiền gửi (VND)', 'Mở thẻ (HĐ)', 'TÌNH TRẠNG']);
    const quarter = root(fixture).querySelector('.quarter-row')!;
    expect(text(quarter)).toContain('Quý 4/2026');
    expect(text(quarter)).toContain('64');
    expect(text(quarter)).toContain('Chờ xác nhận');
    expect(root(fixture).querySelectorAll('.month-row').length).toBe(2);
    expect(text(root(fixture).querySelector('.total-row'))).toContain('Cả năm 2020');
  });

  it('asks for the manager name and the confirm button while the quarter waits', async () => {
    const fixture = await create();

    expect(text(root(fixture).querySelector('h1'))).toContain('Chờ xác nhận Quý 4');
    expect(text(root(fixture).querySelector('.subtitle'))).toContain(
      'Trưởng phòng Phạm Thu Hà giao lúc',
    );
    expect(text(root(fixture).querySelector('#my-targets-confirm'))).toBe(
      'Xác nhận mục tiêu Quý 4',
    );
    expect(root(fixture).querySelector('#my-targets-comment')).not.toBeNull();
    const sendButtons = Array.from(root(fixture).querySelectorAll('button')).filter(
      (button) => text(button) === 'Gửi góp ý',
    );
    expect(sendButtons.length).withContext('only the button of the feedback box sends').toBe(1);
  });

  it('confirms every unconfirmed target of the quarter, then reloads', async () => {
    targets.confirm.and.returnValue(of(feedback('CONFIRM')));
    const fixture = await create();

    click(fixture, '#my-targets-confirm');
    fixture.detectChanges();

    expect(targets.confirm.calls.allArgs()).toEqual([['t1'], ['t2']]);
    expect(targets.mine).toHaveBeenCalledTimes(2);
    expect(text(root(fixture).querySelector('.toast'))).toContain('Đã xác nhận mục tiêu Quý 4');
  });

  it('treats a 409 (already confirmed) as done and shows an error for any other failure', async () => {
    targets.confirm.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    const fixture = await create();
    click(fixture, '#my-targets-confirm');
    fixture.detectChanges();
    expect(text(root(fixture).querySelector('.toast'))).toContain('Đã xác nhận mục tiêu');

    targets.confirm.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    click(fixture, '#my-targets-confirm');
    fixture.detectChanges();
    expect(root(fixture).querySelector('.toast')?.classList).toContain('toast-error');
  });

  it('shows the banner and the re-confirm button when the manager changed a confirmed target', async () => {
    const fixture = await create([feedback('CONFIRM')]);

    const banner = root(fixture).querySelector('.banner');
    expect(text(banner)).toContain(
      'Mục tiêu Quý 4 đã được Trưởng phòng cập nhật, vui lòng xác nhận lại.',
    );
    expect(text(banner)).toContain('Trạng thái quay về Chờ xác nhận.');
    expect(text(root(fixture).querySelector('h1'))).toContain('Đã xác nhận Quý 4');
    expect(root(fixture).querySelector('#my-targets-reconfirm')).not.toBeNull();
    expect(root(fixture).querySelector('#my-targets-confirm')).toBeNull();
  });

  it('shows no banner for a quarter that was never confirmed', async () => {
    const fixture = await create([]);

    expect(root(fixture).querySelector('.banner')).toBeNull();
  });

  it('sends the feedback to every KPI target and clears the box', async () => {
    targets.comment.and.returnValue(of(feedback('COMMENT', 'Cần xem lại')));
    const fixture = await create();
    const box = root(fixture).querySelector<HTMLTextAreaElement>('#my-targets-comment')!;
    box.value = '  Cần xem lại  ';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(text(root(fixture).querySelector('.counter'))).toBe('11 / 500');

    click(fixture, '#my-targets-send');
    fixture.detectChanges();

    expect(targets.comment.calls.allArgs()).toEqual([
      ['t1', 'Cần xem lại'],
      ['t2', 'Cần xem lại'],
    ]);
    expect(text(root(fixture).querySelector('.toast'))).toContain('Đã gửi góp ý');
  });

  it('refuses an empty feedback without calling the API', async () => {
    const fixture = await create();

    click(fixture, '#my-targets-send');

    expect(targets.comment).not.toHaveBeenCalled();
    expect(text(root(fixture).querySelector('.form-error'))).toBe('Vui lòng nhập góp ý.');
  });

  it('lists earlier feedback and counts it', async () => {
    const fixture = await create([feedback('COMMENT', 'Số liệu chưa khớp')]);

    expect(text(root(fixture).querySelector('.feedback-card header'))).toContain(
      'Lịch sử góp ý: 1',
    );
    expect(text(root(fixture).querySelector('.comment-list'))).toContain('Số liệu chưa khớp');
  });

  it('hides the buttons and the feedback box once the quarter is confirmed', async () => {
    current = current.map((t) => ({
      ...t,
      feedbackStatus: 'CONFIRMED' as const,
      feedbackAt: '2026-10-21T07:02:11+00:00',
    }));
    const fixture = await create();

    expect(text(root(fixture).querySelector('h1'))).toContain('Đã xác nhận Quý 4');
    expect(text(root(fixture).querySelector('.subtitle'))).toContain('Xác nhận lúc');
    expect(root(fixture).querySelector('#my-targets-confirm')).toBeNull();
    expect(root(fixture).querySelector('#my-targets-comment')).toBeNull();
    expect(text(root(fixture).querySelector('.page-head > .muted'))).toBe(
      'Đã ấn nút Xác nhận và Gửi góp ý',
    );
  });

  it('opens the week panel of the running month and switches to days', async () => {
    targets.breakdown.and.callFake((id: string, granularity?: 'MONTH' | 'WEEK' | 'DAY') =>
      of({
        targetAssignmentId: id,
        periodStart: `${THIS_MONTH}-01`,
        periodEnd: `${NEXT_MONTH}-28`,
        totalValue: 1,
        workingDays: 1,
        granularity: granularity ?? 'MONTH',
        items: [{ start: `${THIS_MONTH}-05`, end: `${THIS_MONTH}-09`, workingDays: 5, value: 10 }],
      }),
    );
    const fixture = await create();
    click(fixture, '.month-row .link-button');
    fixture.detectChanges();

    expect(targets.breakdown).toHaveBeenCalledWith('t1', 'WEEK');
    expect(text(root(fixture).querySelector('.slice-panel'))).toContain(
      `05/${MONTH_NUMBER} – 09/${MONTH_NUMBER}`,
    );

    click(fixture, '.slice-panel .segmented button:last-child');
    expect(targets.breakdown).toHaveBeenCalledWith('t1', 'DAY');
  });

  it('shows an error with a retry when the targets cannot be loaded', async () => {
    targets.mine.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const fixture = await create();

    expect(text(root(fixture).querySelector('.state-error'))).toContain('Không tải được mục tiêu');
    targets.mine.and.callFake(() => of(current));
    click(fixture, '.state-error button');
    fixture.detectChanges();
    expect(root(fixture).querySelector('.targets-table')).not.toBeNull();
  });

  it('says so when the employee has no quarterly target', async () => {
    current = [];
    const fixture = await create();

    expect(text(root(fixture).querySelector('.state-card'))).toBe(
      'Bạn chưa được giao mục tiêu quý nào.',
    );
  });
});
