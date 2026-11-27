import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { CampaignCustomer } from '../../core/campaign-customer.models';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { UpdateContactModalComponent } from './update-contact-modal.component';

const customer: CampaignCustomer = {
  id: 'k1',
  customerCif: '0012345',
  customerName: 'Nguyễn Văn B',
  phone: '0901234567',
  note: 'VIP',
  assigneeEmployeeId: 'e1',
  assigneeEmployeeName: 'Đỗ Minh Khang',
  contactStatus: 'CALLED',
  resultText: 'Không nghe máy 2 lần',
  missedCallCount: 2,
  appointmentAt: null,
  metAt: null,
  closedWorkTypeId: null,
  closedWorkTypeName: null,
  workId: 'w1',
  workStatus: 'IN_PROGRESS',
  updatedAt: '2026-11-23T10:00:00Z',
};

describe('UpdateContactModalComponent', () => {
  const api = jasmine.createSpyObj<CampaignCustomerService>('CampaignCustomerService', [
    'updateContact',
  ]);
  let fixture: ComponentFixture<UpdateContactModalComponent>;
  let root: HTMLElement;
  let saved: CampaignCustomer[];

  const open = async (value: CampaignCustomer = customer) => {
    fixture = TestBed.createComponent(UpdateContactModalComponent);
    fixture.componentRef.setInput('campaignId', 'c1');
    fixture.componentRef.setInput('customer', value);
    saved = [];
    fixture.componentInstance.saved.subscribe((item) => saved.push(item));
    fixture.detectChanges();
    await fixture.whenStable();
    root = fixture.nativeElement;
  };

  const chooseStatus = (value: string) => {
    const select = root.querySelector<HTMLSelectElement>('#contact-status')!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };

  const save = () => {
    root.querySelector<HTMLButtonElement>('.modal-footer .button-primary')!.click();
    fixture.detectChanges();
  };

  const type = async (selector: string, value: string) => {
    const field = root.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
  };

  beforeEach(async () => {
    api.updateContact.calls.reset();
    api.updateContact.and.returnValue(of(customer));
    await TestBed.configureTestingModule({
      imports: [UpdateContactModalComponent],
      providers: [{ provide: CampaignCustomerService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('shows the current status and the missed-call count', async () => {
    await open();

    expect(root.textContent).toContain('Đã gọi');
    expect(root.textContent).toContain('Không nghe máy 2 lần');
    expect(root.textContent).toContain('Đã gọi không nghe máy: 2 lần');
  });

  it('requires the call result before saving a call', async () => {
    await open();

    save();

    expect(api.updateContact).not.toHaveBeenCalled();
    expect(root.querySelector('.field-error')?.textContent).toContain('bắt buộc');
  });

  it('saves a missed call with callAnswered false', async () => {
    await open();

    root.querySelectorAll<HTMLInputElement>('input[name="call-result"]')[1].click();
    fixture.detectChanges();
    save();

    expect(api.updateContact).toHaveBeenCalledWith('c1', 'k1', {
      status: 'CALLED',
      callAnswered: false,
      appointmentAt: null,
      metAt: null,
      transactionCodes: null,
      note: null,
    });
    expect(saved.length).toBe(1);
  });

  it('requires a date and time for a meeting', async () => {
    await open();
    chooseStatus('APPOINTMENT_SET');

    save();

    expect(api.updateContact).not.toHaveBeenCalled();
    expect(root.querySelector('#contact-when')).not.toBeNull();
    expect(root.querySelector('.field-error')?.textContent).toContain('bắt buộc');
  });

  it('asks for transaction codes, then confirms before closing', async () => {
    await open();
    chooseStatus('CLOSED');
    expect(root.textContent).toContain('không thể sửa lại');

    save();
    expect(api.updateContact).not.toHaveBeenCalled();
    expect(root.querySelector('.field-error')?.textContent).toContain('1 đến 10 mã');

    await type('#contact-codes', 'TX-1\nTX-2, TX-1');
    save();
    expect(api.updateContact).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('2 mã giao dịch');

    save();
    expect(api.updateContact).toHaveBeenCalledWith('c1', 'k1', {
      status: 'CLOSED',
      callAnswered: null,
      appointmentAt: null,
      metAt: null,
      transactionCodes: ['TX-1', 'TX-2'],
      note: null,
    });
  });

  it('rejects more than ten transaction codes', async () => {
    await open();
    chooseStatus('CLOSED');

    await type('#contact-codes', Array.from({ length: 11 }, (_, i) => `TX-${i}`).join(' '));
    save();

    expect(api.updateContact).not.toHaveBeenCalled();
    expect(root.querySelector('.field-error')?.textContent).toContain('Tối đa 10');
  });

  it('shows the server error for a duplicate transaction code and leaves the confirm step', async () => {
    api.updateContact.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { success: false, errorCode: 'WORK_TRANSACTION_DUPLICATE' },
          }),
      ),
    );
    await open();
    chooseStatus('CLOSED');
    await type('#contact-codes', 'TX-1');
    save();
    save();

    expect(root.querySelector('.notice.is-error')?.textContent).toContain('đã được dùng');
    expect(saved.length).toBe(0);
    expect(root.querySelector('#contact-codes')).not.toBeNull();
  });
});
