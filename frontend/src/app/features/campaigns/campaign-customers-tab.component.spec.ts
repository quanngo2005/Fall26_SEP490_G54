import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CampaignCustomer, CampaignCustomerList } from '../../core/campaign-customer.models';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { CampaignDetail } from '../../core/campaign.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { CampaignCustomersTabComponent } from './campaign-customers-tab.component';

const customer = (over: Partial<CampaignCustomer>): CampaignCustomer => ({
  id: 'k1',
  customerCif: '0012345',
  customerName: 'Nguyễn Văn B',
  phone: '0901234567',
  note: null,
  assigneeEmployeeId: 'e1',
  assigneeEmployeeName: 'Lê Thị Thảo',
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
  ...over,
});

const list: CampaignCustomerList = {
  page: {
    items: [
      customer({}),
      customer({
        id: 'k2',
        customerName: 'Phạm Văn E',
        contactStatus: 'CLOSED',
        resultText: 'Đã chốt được sản phẩm Tiền gửi 12 tháng',
        workStatus: 'REJECTED',
      }),
      customer({ id: 'k3', assigneeEmployeeId: null, assigneeEmployeeName: null }),
    ],
    totalCount: 48,
    page: 1,
    pageSize: 20,
  },
  summary: {
    total: 48,
    unassigned: 6,
    byStatus: [{ status: 'CLOSED', count: 9 }],
    byAssignee: [
      {
        employeeId: 'e1',
        employeeCode: 'NV0231',
        employeeName: 'Lê Thị Thảo',
        assigned: 12,
        closed: 4,
      },
    ],
  },
};

const campaign = (status: string) =>
  ({
    id: 'c1',
    code: 'CMP-2026-06',
    status,
    participants: [{ employeeId: 'e1', employeeCode: 'NV0231', fullName: 'Lê Thị Thảo' }],
    metrics: { customerTotal: 48, customerUnassigned: 6, customerClosed: 9 },
  }) as unknown as CampaignDetail;

describe('CampaignCustomersTabComponent', () => {
  const api = jasmine.createSpyObj<CampaignCustomerService>('CampaignCustomerService', [
    'list',
    'reassign',
    'downloadTemplate',
  ]);

  const open = async (status = 'RUNNING', canManage = true) => {
    const fixture = TestBed.createComponent(CampaignCustomersTabComponent);
    fixture.componentRef.setInput('campaign', campaign(status));
    fixture.componentRef.setInput('canManage', canManage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  };

  beforeEach(async () => {
    api.list.calls.reset();
    api.reassign.calls.reset();
    api.list.and.returnValue(of(list));
    api.reassign.and.returnValue(of(customer({})));
    await TestBed.configureTestingModule({
      imports: [CampaignCustomersTabComponent],
      providers: [{ provide: CampaignCustomerService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('shows conversion with units and progress per employee', async () => {
    const { root } = await open();

    expect(root.querySelector('.summary-grid')?.textContent).toContain('9/48');
    expect(root.querySelector('.summary-grid')?.textContent).toContain('(19%)');
    expect(root.textContent).toContain('4/12 đã chốt (33%)');
    expect(api.list).toHaveBeenCalledTimes(1);
  });

  it('locks the assignee of a closed row and labels unassigned ones', async () => {
    const { root } = await open();

    const rows = root.querySelectorAll('.list-toolbar ~ .table-wrap tbody tr');
    expect(rows[1].querySelector('select')).toBeNull();
    expect(rows[1].textContent).toContain('Lê Thị Thảo');
    expect(rows[1].textContent).toContain('Kết quả bị từ chối');
    expect(rows[2].querySelector('select option')?.textContent).toContain('Chưa phân bổ');
  });

  it('reassigns a customer from the inline select', async () => {
    const { fixture, root } = await open();

    const selects = root.querySelectorAll<HTMLSelectElement>(
      '.list-toolbar ~ .table-wrap tbody select',
    );
    selects[0].value = 'e1';
    selects[0].dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(api.reassign).not.toHaveBeenCalled();

    selects[1].value = 'e1';
    selects[1].dispatchEvent(new Event('change'));
    expect(api.reassign).toHaveBeenCalledWith('c1', 'k3', 'e1');
  });

  it('disables import, allocate and template with a reason when the campaign has ended', async () => {
    const { root } = await open('COMPLETED');

    const buttons = root.querySelectorAll<HTMLButtonElement>('.toolbar-actions button');
    expect(buttons.length).toBe(3);
    buttons.forEach((button) => expect(button.disabled).toBe(true));
    expect(root.textContent).toContain('không thể thay đổi danh sách');
    expect(root.querySelector('tbody select')).toBeNull();
  });

  it('hides the toolbar from someone who cannot manage the campaign', async () => {
    const { root } = await open('RUNNING', false);

    expect(root.querySelector('.toolbar-actions')).toBeNull();
  });

  it('opens the allocate modal with the unassigned count', async () => {
    const { fixture, root } = await open();

    root.querySelectorAll<HTMLButtonElement>('.toolbar-actions button')[2].click();
    fixture.detectChanges();

    expect(root.querySelector('app-campaign-customer-allocate-modal')).not.toBeNull();
    expect(root.querySelector('.count-line')?.textContent).toContain('6');
  });
});
