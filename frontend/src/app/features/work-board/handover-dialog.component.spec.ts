import { HttpErrorResponse } from '@angular/common/http';
import { ComponentRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { HandoverDetail, HandoverRecipient } from '../../core/handover.models';
import { HandoverService } from '../../core/handover.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { WorkListItem } from '../../core/work.models';
import { HandoverDialogComponent } from './handover-dialog.component';

const work = (id: string): WorkListItem =>
  ({ id, code: `WK-${id}`, title: `Việc ${id}`, status: 'New' }) as WorkListItem;

const recipient: HandoverRecipient = {
  id: 'e2',
  employeeCode: 'NV002',
  fullName: 'Trần Thị B',
  jobPositionTitle: 'Giao dịch viên',
};

describe('HandoverDialogComponent', () => {
  const api = { recipients: jasmine.createSpy('recipients'), create: jasmine.createSpy('create') };
  let fixture: ComponentFixture<HandoverDialogComponent>;
  let element: HTMLElement;

  async function setup(recipients = of([recipient])): Promise<void> {
    api.recipients.and.returnValue(recipients);
    api.create.calls.reset();
    await TestBed.configureTestingModule({
      imports: [HandoverDialogComponent],
      providers: [{ provide: HandoverService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    fixture = TestBed.createComponent(HandoverDialogComponent);
    const ref: ComponentRef<HandoverDialogComponent> = fixture.componentRef;
    ref.setInput('works', [work('1')]);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

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

  const fillValid = async () => {
    await choose('#handover-work', '1');
    await choose('#handover-recipient', 'e2');
    await choose('#handover-reason', 'Nghỉ phép');
  };

  it('lists the works and the colleagues it can hand over to', async () => {
    await setup();

    expect(element.querySelectorAll('#handover-work option').length).toBe(2);
    expect(element.querySelector('#handover-recipient')?.textContent).toContain('Trần Thị B');
  });

  it('refuses to send until the required fields are filled', async () => {
    await setup();

    submit();

    expect(api.create).not.toHaveBeenCalled();
    expect(element.querySelectorAll('.field-error').length).toBe(3);
  });

  it('sends the request and reports the created handover', async () => {
    await setup();
    const created = { id: 'h1', workCode: 'WK-1', toEmployeeName: 'Trần Thị B' } as HandoverDetail;
    api.create.and.returnValue(of(created));
    let emitted: HandoverDetail | undefined;
    fixture.componentInstance.requested.subscribe((value) => (emitted = value));
    await fillValid();

    submit();

    expect(api.create).toHaveBeenCalledOnceWith('1', {
      toEmployeeId: 'e2',
      reason: 'Nghỉ phép',
      amount: 0,
    });
    expect(emitted).toBe(created);
  });

  it('rejects a negative amount', async () => {
    await setup();
    await fillValid();
    await choose('#handover-amount', '-5');

    submit();

    expect(api.create).not.toHaveBeenCalled();
    expect(element.querySelector('.field-error')?.textContent).toContain('lớn hơn hoặc bằng 0');
  });

  it('explains the errors the server can answer with', async () => {
    await setup();
    await fillValid();
    const fail = (status: number, errorCode?: string) => {
      api.create.and.returnValue(
        throwError(() => new HttpErrorResponse({ status, error: { errorCode } })),
      );
      submit();
      return element.querySelector('.form-error')?.textContent ?? '';
    };

    expect(fail(409, 'HANDOVER_ALREADY_PENDING')).toContain('chờ phản hồi');
    expect(fail(409, 'WORK_INVALID_STATE')).toContain('không còn ở trạng thái');
    expect(fail(422, 'HANDOVER_AMOUNT_EXCEEDS_THRESHOLD')).toContain('vượt ngưỡng');
    expect(fail(403)).toContain('không có quyền');
    expect(fail(400)).toContain('không hợp lệ');
    expect(fail(500)).toContain('Không gửi được');
  });

  it('shows a message when the colleague list cannot be loaded', async () => {
    await setup(throwError(() => new HttpErrorResponse({ status: 500 })));

    expect(element.querySelector('.field-error')?.textContent).toContain('đồng nghiệp');
  });

  it('closes on Escape', async () => {
    await setup();
    let closed = false;
    fixture.componentInstance.closed.subscribe(() => (closed = true));

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(closed).toBeTrue();
  });
});
