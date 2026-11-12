import { HttpErrorResponse } from '@angular/common/http';
import { ComponentRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { EmployeeService } from '../../core/employee.service';
import { HandoverService } from '../../core/handover.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { MySubmissionService } from '../../core/my-submission.service';
import { ResultSubmissionService } from '../../core/result-submission.service';
import { WorkDetail } from '../../core/work.models';
import { WorkService } from '../../core/work.service';
import { WorkDetailDrawerComponent } from './work-detail-drawer.component';

const detail = (overrides: Partial<WorkDetail> = {}): WorkDetail =>
  ({
    id: 'w1',
    code: 'WK-1038',
    title: 'E-banking - Đăng ký',
    description: 'Hướng dẫn khách đăng ký, kích hoạt E-banking.',
    workTypeId: 't1',
    workTypeName: 'Dịch vụ · E-banking',
    businessCampaignId: null,
    businessCampaignName: null,
    assigneeEmployeeId: 'emp-a',
    assigneeEmployeeName: 'Nguyễn Văn A',
    assigneeEmployeeCode: 'NV001',
    priority: 'High',
    status: 'InProgress',
    plannedStartDate: '2026-10-24',
    plannedEndDate: '2099-12-31',
    estimatedHours: null,
    updatedAt: '2026-10-24T07:32:00+00:00',
    customerName: 'Trần Thị B',
    customerCif: '0012345',
    assignedAt: '2026-10-24T02:10:00+00:00',
    assignedByEmployeeId: 'emp-c',
    assignedByName: 'Lê Văn C',
    assignedByPositionTitle: 'Trưởng phòng',
    checklistItems: [
      {
        id: 'c1',
        title: 'Xác minh giấy tờ',
        isDone: true,
        sortOrder: 0,
        doneAt: '2026-10-24T03:00:00Z',
      },
      { id: 'c2', title: 'Kích hoạt OTP', isDone: false, sortOrder: 1, doneAt: null },
    ],
    progressPercent: 50,
    relatedKpis: [{ code: 'KPI-EBK-P', name: 'E-banking', creditRole: 'PROCESSOR' }],
    canEditPlan: false,
    canUpdateChecklist: true,
    ...overrides,
  }) as WorkDetail;

describe('WorkDetailDrawerComponent', () => {
  const works = {
    detail: jasmine.createSpy('detail'),
    activities: jasmine.createSpy('activities'),
    setChecklistItem: jasmine.createSpy('setChecklistItem'),
  };
  const submissions = { getSubmissionsByWork: jasmine.createSpy('getSubmissionsByWork') };
  const mySubmissions = { detail: jasmine.createSpy('detail') };
  const employees = { me: jasmine.createSpy('me') };
  let fixture: ComponentFixture<WorkDetailDrawerComponent>;
  let element: HTMLElement;
  let changed: number;

  async function setup(
    options: { work?: WorkDetail; me?: string; detailError?: HttpErrorResponse } = {},
  ): Promise<void> {
    [works, submissions, mySubmissions, employees].forEach((group) =>
      Object.values(group).forEach((spy) => spy.calls.reset()),
    );
    works.detail.and.returnValue(
      options.detailError ? throwError(() => options.detailError) : of(options.work ?? detail()),
    );
    works.activities.and.returnValue(
      of([
        {
          id: 'a2',
          action: 'STATUS_CHANGED',
          message: 'Chuyển sang Đang thực hiện',
          actorEmployeeId: 'emp-a',
          actorName: 'Nguyễn Văn A',
          createdAt: '2026-10-24T03:02:00+00:00',
        },
        {
          id: 'a1',
          action: 'ASSIGNED',
          message: 'Việc được giao',
          actorEmployeeId: 'emp-c',
          actorName: 'Lê Văn C',
          createdAt: '2026-10-24T02:10:00+00:00',
        },
      ]),
    );
    submissions.getSubmissionsByWork.and.returnValue(of([]));
    employees.me.and.returnValue(of({ id: options.me ?? 'emp-a' }));
    changed = 0;

    await TestBed.configureTestingModule({
      imports: [WorkDetailDrawerComponent],
      providers: [
        { provide: WorkService, useValue: works },
        { provide: ResultSubmissionService, useValue: submissions },
        { provide: MySubmissionService, useValue: mySubmissions },
        { provide: EmployeeService, useValue: employees },
        { provide: HandoverService, useValue: { recipients: () => of([]) } },
      ],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    await create();
  }

  async function create(): Promise<void> {
    fixture = TestBed.createComponent(WorkDetailDrawerComponent);
    const ref: ComponentRef<WorkDetailDrawerComponent> = fixture.componentRef;
    ref.setInput('workId', 'w1');
    fixture.componentInstance.changed.subscribe(() => changed++);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const buttons = () =>
    Array.from(element.querySelectorAll('.drawer-actions button')).map((b) =>
      b.textContent?.trim(),
    );

  it('shows all the information of the work in the panel', async () => {
    await setup();

    expect(element.querySelector('.drawer-title')?.textContent).toContain('E-banking - Đăng ký');
    expect(element.querySelector('.drawer-code')?.textContent).toContain('WK-1038');
    expect(element.textContent).toContain('Trần Thị B');
    expect(element.textContent).toContain('0012345');
    expect(element.textContent).toContain('KPI-EBK-P');
    expect(element.textContent).toContain('Do Trưởng phòng giao');
    expect(element.textContent).toContain('Giao bởi Lê Văn C (Trưởng phòng)');
    expect(element.textContent).toContain('Hướng dẫn khách đăng ký');
    expect(element.querySelector('.percent')?.textContent).toContain('50%');
    expect(element.querySelectorAll('.checklist input').length).toBe(2);
    expect(element.textContent).toContain('Việc được giao');
  });

  it('gives the assignee hand-over and submit, and no plan editing', async () => {
    await setup();

    expect(buttons()).toEqual(['Chuyển giao việc', 'Nộp kết quả']);
    expect(element.textContent).toContain('không cần chấp nhận hay từ chối');
  });

  it('gives the manager "Sửa kế hoạch" and a read-only checklist', async () => {
    await setup({ work: detail({ canEditPlan: true, canUpdateChecklist: false }), me: 'emp-c' });

    expect(buttons()).toEqual(['Sửa kế hoạch']);
    expect(element.textContent).toContain('Người thực hiện: Nguyễn Văn A');
    expect(element.querySelectorAll('.checklist input:not(:disabled)').length).toBe(0);
  });

  it('ticks an item through the API, shows the new progress and tells the board', async () => {
    await setup();
    works.setChecklistItem.and.returnValue(
      of(
        detail({
          progressPercent: 100,
          checklistItems: detail().checklistItems!.map((i) => ({ ...i, isDone: true })),
        }),
      ),
    );

    const box = element.querySelectorAll<HTMLInputElement>('.checklist input')[1];
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(works.setChecklistItem).toHaveBeenCalledOnceWith('w1', 'c2', true);
    expect(element.querySelector('.percent')?.textContent).toContain('100%');
    expect(changed).toBe(1);
  });

  it('puts the box back and says so when ticking fails', async () => {
    await setup();
    works.setChecklistItem.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );

    const box = element.querySelectorAll<HTMLInputElement>('.checklist input')[1];
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(box.checked).toBeFalse();
    expect(element.querySelector('.notice-bar')?.textContent).toContain(
      'Chưa cập nhật được mục này',
    );
    expect(changed).toBe(0);
  });

  it('offers a retry when the work cannot be loaded', async () => {
    await setup({ detailError: new HttpErrorResponse({ status: 500 }) });

    expect(element.querySelector('.drawer-error')?.textContent).toContain('Không tải được');
    expect(element.querySelector('.drawer-error button')?.textContent).toContain('Tải lại');
  });

  it('lists the versions already submitted, matched or not, for the assignee', async () => {
    await setup();
    submissions.getSubmissionsByWork.and.returnValue(
      of([
        {
          id: 's1',
          submissionNo: 1,
          status: 'SUBMITTED',
          submittedAt: '2026-10-24T08:00:00+00:00',
        },
        {
          id: 's2',
          submissionNo: 2,
          status: 'SUBMITTED',
          submittedAt: '2026-10-24T09:00:00+00:00',
        },
      ]),
    );
    mySubmissions.detail.and.returnValue(
      of({
        submissionId: 's2',
        transactions: [{ transactionCode: 'GD-778899' }],
        versions: [
          { submissionId: 's1', matchStatus: 'NOT_MATCHED' },
          { submissionId: 's2', matchStatus: 'MATCHED' },
        ],
      }),
    );

    await create();

    const rows = Array.from(element.querySelectorAll('.submission-list li')).map(
      (r) => r.textContent ?? '',
    );
    expect(rows.length).toBe(2);
    expect(rows[0]).toContain('GD-778899');
    expect(rows[0]).toContain('Đã khớp');
    expect(rows[1]).toContain('Chưa khớp');
  });

  it('closes on Escape, but leaves the panel open while a dialog is open', async () => {
    await setup();
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);

    (element.querySelector('.drawer-actions button') as HTMLButtonElement).click();
    fixture.detectChanges();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(closed).toBe(0);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(closed).toBe(1);
  });
});
