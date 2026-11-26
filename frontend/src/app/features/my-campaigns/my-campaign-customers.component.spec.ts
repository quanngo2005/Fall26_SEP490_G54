import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { CampaignCustomer, MyCampaign } from '../../core/campaign-customer.models';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { MyCampaignCustomersComponent } from './my-campaign-customers.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const customer = (over: Partial<CampaignCustomer>): CampaignCustomer => ({
  id: 'k1',
  customerCif: '0012345',
  customerName: 'Nguyễn Văn B',
  phone: null,
  note: null,
  assigneeEmployeeId: 'e1',
  assigneeEmployeeName: 'Khang',
  contactStatus: 'CALLED',
  resultText: 'Gọi thành công',
  missedCallCount: 0,
  appointmentAt: null,
  metAt: null,
  closedWorkTypeId: null,
  closedWorkTypeName: null,
  workId: 'w1',
  workStatus: 'IN_PROGRESS',
  updatedAt: '2026-11-23T10:00:00Z',
  ...over,
});

const mine = (status: string): MyCampaign => ({
  campaignId: 'c1',
  campaignCode: 'CMP-2026-06',
  campaignName: 'Tiền gửi cuối năm',
  status,
  startDate: '2026-10-01',
  endDate: '2026-12-31',
  totalAssigned: 3,
  closed: 1,
  byStatus: [],
});

describe('MyCampaignCustomersComponent', () => {
  const api = jasmine.createSpyObj<CampaignCustomerService>('CampaignCustomerService', [
    'myCampaigns',
    'myCustomers',
  ]);

  const open = async (status = 'RUNNING', campaigns = [mine(status)]) => {
    api.myCampaigns.and.returnValue(of(campaigns));
    api.myCustomers.and.returnValue(
      of({
        items: [
          customer({ id: 'k2', customerName: 'Đã chốt', contactStatus: 'CLOSED' }),
          customer({ id: 'k3', customerName: 'Chưa gọi', contactStatus: 'NOT_CONTACTED' }),
        ],
        totalCount: 2,
        page: 1,
        pageSize: 20,
      }),
    );
    await TestBed.configureTestingModule({
      imports: [MyCampaignCustomersComponent],
      providers: [
        { provide: CampaignCustomerService, useValue: api },
        provideRouter([{ path: 'my-campaigns/:id', component: MyCampaignCustomersComponent }]),
      ],
    })
      .overrideComponent(MyCampaignCustomersComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/my-campaigns/c1', MyCampaignCustomersComponent);
    harness.detectChanges();
    return harness.routeNativeElement as HTMLElement;
  };

  it('lists customers needing action first and closed ones last', async () => {
    const root = await open();

    const rows = root.querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('Chưa gọi');
    expect(rows[1].textContent).toContain('Đã chốt');
    expect(api.myCustomers).toHaveBeenCalledWith(
      'c1',
      jasmine.objectContaining({ page: 1, pageSize: 20 }),
    );
  });

  it('replaces Update with a link to the work on a closed row', async () => {
    const root = await open();

    const rows = root.querySelectorAll('tbody tr');
    expect(rows[0].querySelector('button')?.textContent).toContain('Cập nhật');
    expect(rows[1].querySelector('button')).toBeNull();
    expect(rows[1].querySelector('a')?.textContent).toContain('Mở việc');
  });

  it('is read-only with a reason once the campaign stops running', async () => {
    TestBed.resetTestingModule();
    const root = await open('COMPLETED');

    expect(root.textContent).toContain('chỉ xem');
    expect(root.querySelector<HTMLButtonElement>('tbody button')!.disabled).toBe(true);
  });

  it('shows a not-found message instead of an empty table when the campaign is not mine', async () => {
    TestBed.resetTestingModule();
    const root = await open('RUNNING', []);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'Không tìm thấy chiến dịch',
    );
    expect(root.querySelector('table')).toBeNull();
  });
});
