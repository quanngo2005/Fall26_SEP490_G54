import { HttpErrorResponse } from '@angular/common/http';
import { ComponentRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { WorkDetail } from '../../core/work.models';
import { WorkService } from '../../core/work.service';
import { WorkPlanDialogComponent } from './work-plan-dialog.component';

const work = {
  id: 'w1',
  code: 'WK-1038',
  title: 'E-banking - Đăng ký',
  assigneeEmployeeName: 'Nguyễn Văn A',
  priority: 'High',
  status: 'InProgress',
  plannedStartDate: '2026-10-24',
  plannedEndDate: '2026-10-25',
  checklistItems: [
    {
      id: 'c1',
      title: 'Xác minh giấy tờ',
      isDone: true,
      sortOrder: 0,
      doneAt: '2026-10-24T03:00:00Z',
    },
    {
      id: 'c2',
      title: 'Tạo tài khoản E-banking',
      isDone: true,
      sortOrder: 1,
      doneAt: '2026-10-24T04:00:00Z',
    },
    { id: 'c3', title: 'Kích hoạt OTP', isDone: false, sortOrder: 2, doneAt: null },
    { id: 'c4', title: 'Hướng dẫn khách sử dụng', isDone: false, sortOrder: 3, doneAt: null },
  ],
} as unknown as WorkDetail;

describe('WorkPlanDialogComponent', () => {
  const api = { updatePlan: jasmine.createSpy('updatePlan') };
  let fixture: ComponentFixture<WorkPlanDialogComponent>;
  let element: HTMLElement;
  let saved: WorkDetail[];

  async function setup(): Promise<void> {
    api.updatePlan.calls.reset();
    api.updatePlan.and.returnValue(of({ ...work, plannedEndDate: '2026-10-27' }));
    saved = [];
    await TestBed.configureTestingModule({
      imports: [WorkPlanDialogComponent],
      providers: [{ provide: WorkService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    fixture = TestBed.createComponent(WorkPlanDialogComponent);
    const ref: ComponentRef<WorkPlanDialogComponent> = fixture.componentRef;
    ref.setInput('work', work);
    fixture.componentInstance.saved.subscribe((w) => saved.push(w));
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await fixture.whenStable();
  }

  const submit = () => {
    element.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
    fixture.detectChanges();
  };

  it('starts from the current plan: deadline, priority and the checklist with done items locked', async () => {
    await setup();

    expect(element.querySelector<HTMLInputElement>('#plan-due')?.value).toBe('2026-10-25');
    expect(element.querySelectorAll('.plan-row.is-locked').length).toBe(2);
    expect(element.querySelectorAll('.plan-row:not(.is-locked)').length).toBe(2);
    expect(element.textContent).toContain('2/4');
    expect(element.textContent).toContain('Đã xong, không sửa');
  });

  it('sends the whole checklist, keeping ids of existing items and none for a new one', async () => {
    await setup();
    const component = fixture.componentInstance as unknown as {
      addItem(): void;
      rename(index: number, title: string): void;
      removeItem(index: number): void;
      dueDate: string;
      note: string;
    };

    component.removeItem(1);
    component.addItem();
    component.rename(1, '  Gửi tài liệu qua email ');
    component.dueDate = '2026-10-27';
    component.note = '  Khách hẹn lại ngày 27/10  ';
    fixture.detectChanges();
    submit();

    expect(api.updatePlan).toHaveBeenCalledOnceWith('w1', {
      plannedEndDate: '2026-10-27',
      priority: 'High',
      checklist: [
        { id: 'c1', title: 'Xác minh giấy tờ' },
        { id: 'c2', title: 'Tạo tài khoản E-banking' },
        { id: 'c3', title: 'Kích hoạt OTP' },
        { id: undefined, title: 'Gửi tài liệu qua email' },
      ],
      note: 'Khách hẹn lại ngày 27/10',
    });
    expect(saved.length).toBe(1);
  });

  it('does not send a note when it is blank', async () => {
    await setup();

    submit();

    expect(api.updatePlan.calls.mostRecent().args[1].note).toBeUndefined();
  });

  it('asks for the deadline and for a name on every open item before saving', async () => {
    await setup();
    const component = fixture.componentInstance as unknown as {
      addItem(): void;
      dueDate: string;
    };

    component.dueDate = '';
    component.addItem();
    fixture.detectChanges();
    submit();

    expect(api.updatePlan).not.toHaveBeenCalled();
    expect(element.textContent).toContain('Bạn chọn hạn hoàn thành giúp mình nhé.');
    expect(element.textContent).toContain('Tên đầu việc không được để trống.');
  });

  it('shows a plain message when the work can no longer be edited', async () => {
    await setup();
    api.updatePlan.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));

    submit();
    fixture.detectChanges();

    expect(element.querySelector('.form-error')?.textContent).toContain(
      'đã nộp hoặc đã đóng nên không sửa kế hoạch được nữa',
    );
    expect(saved.length).toBe(0);
  });

  it('shows the server message for invalid details', async () => {
    await setup();
    api.updatePlan.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { message: 'Mục đã xong không thể sửa hoặc xóa: Xác minh giấy tờ.' },
          }),
      ),
    );

    submit();
    fixture.detectChanges();

    expect(element.querySelector('.form-error')?.textContent).toContain(
      'Mục đã xong không thể sửa',
    );
  });

  it('closes on Escape unless it is saving', async () => {
    await setup();
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(closed).toBe(1);
  });
});
