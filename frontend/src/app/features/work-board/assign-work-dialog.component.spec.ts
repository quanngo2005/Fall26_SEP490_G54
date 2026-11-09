import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ComponentRef } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { DepartmentMember } from '../../core/department.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { WorkTypeDetail } from '../../core/work-type.models';
import { WorkDetail } from '../../core/work.models';
import { WorkService } from '../../core/work.service';
import { AssignWorkDialogComponent } from './assign-work-dialog.component';

const member = (id: string, status: DepartmentMember['status'] = 'ACTIVE'): DepartmentMember =>
  ({
    id,
    fullName: `Nhân viên ${id}`,
    jobPositionTitle: 'Giao dịch viên',
    status,
    openWorkCount: 5,
  }) as DepartmentMember;

const workType = (id: string, status: WorkTypeDetail['status'] = 'ACTIVE'): WorkTypeDetail =>
  ({ id, name: `Loại việc ${id}`, status }) as WorkTypeDetail;

describe('AssignWorkDialogComponent', () => {
  const works = { create: jasmine.createSpy('create') };
  let fixture: ComponentFixture<AssignWorkDialogComponent>;
  let element: HTMLElement;

  beforeEach(async () => {
    works.create.calls.reset();
    await TestBed.configureTestingModule({
      imports: [AssignWorkDialogComponent],
      providers: [{ provide: WorkService, useValue: works }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    fixture = TestBed.createComponent(AssignWorkDialogComponent);
    const ref: ComponentRef<AssignWorkDialogComponent> = fixture.componentRef;
    ref.setInput('members', [member('e1'), member('e2', 'INACTIVE')]);
    ref.setInput('workTypes', [workType('t1'), workType('t2', 'INACTIVE')]);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  const choose = async (selector: string, value: string) => {
    const field = element.querySelector<HTMLInputElement | HTMLSelectElement>(selector);
    (field as HTMLInputElement).value = value;
    field?.dispatchEvent(new Event(field instanceof HTMLSelectElement ? 'change' : 'input'));
    fixture.detectChanges();
    await fixture.whenStable();
  };

  const submit = () => {
    element.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
    fixture.detectChanges();
  };

  const tomorrow = () => new Date(Date.now() + 2 * 86_400_000).toISOString().slice(0, 10);

  const fillValid = async () => {
    await choose('#assign-assignee', 'e1');
    await choose('#assign-type', 't1');
    await choose('#assign-due', tomorrow());
  };

  it('offers only active employees and work types', () => {
    const options = (selector: string) =>
      Array.from(element.querySelectorAll<HTMLOptionElement>(`${selector} option`)).map(
        (option) => option.value,
      );

    expect(options('#assign-assignee')).toEqual(['', 'e1']);
    expect(options('#assign-type')).toEqual(['', 't1']);
  });

  it('asks for the required fields before calling the API', () => {
    submit();

    expect(works.create).not.toHaveBeenCalled();
    expect(element.querySelectorAll('.field-error').length).toBe(3);
  });

  it('rejects a due date before today', async () => {
    await fillValid();
    await choose('#assign-due', '2020-01-01');
    submit();

    expect(works.create).not.toHaveBeenCalled();
    expect(element.querySelector('.field-error')?.textContent).toContain(
      'không được trước hôm nay',
    );
  });

  it('creates the work with the chosen values and reports it', async () => {
    const created = { id: 'w1', code: 'WRK-1' } as WorkDetail;
    works.create.and.returnValue(of(created));
    const assigned = jasmine.createSpy('assigned');
    fixture.componentInstance.assigned.subscribe(assigned);
    await fillValid();
    await choose('#assign-priority', 'High');
    element.querySelector<HTMLButtonElement>('.add-item')?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    await choose('.checklist-row input', ' Xác minh giấy tờ ');
    submit();

    expect(works.create).toHaveBeenCalledWith({
      title: 'Loại việc t1',
      workTypeId: 't1',
      assigneeEmployeeId: 'e1',
      priority: 'High',
      plannedEndDate: tomorrow(),
      checklistItems: ['Xác minh giấy tờ'],
    });
    expect(assigned).toHaveBeenCalledWith(created);
  });

  it('keeps the dialog open and explains a refusal', async () => {
    works.create.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
    const assigned = jasmine.createSpy('assigned');
    fixture.componentInstance.assigned.subscribe(assigned);
    await fillValid();
    submit();

    expect(assigned).not.toHaveBeenCalled();
    expect(element.querySelector('.form-error')?.textContent).toContain('không có quyền');
  });

  it('closes on Escape unless a request is running', () => {
    const closed = jasmine.createSpy('closed');
    fixture.componentInstance.closed.subscribe(closed);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(closed).toHaveBeenCalledTimes(1);
  });
});
