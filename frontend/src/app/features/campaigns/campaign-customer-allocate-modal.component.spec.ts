import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { CampaignDetail } from '../../core/campaign.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { CampaignCustomerAllocateModalComponent } from './campaign-customer-allocate-modal.component';

const participant = (id: string, name: string) => ({
  employeeId: id,
  employeeCode: id.toUpperCase(),
  fullName: name,
  jobPositionTitle: null,
  departmentName: null,
  assignedTasksCount: 0,
  completedTasksCount: 0,
});

const campaign = {
  id: 'c1',
  code: 'CMP-2026-06',
  participants: [participant('e1', 'Lê Thị Thảo'), participant('e2', 'Đỗ Minh Khang')],
} as unknown as CampaignDetail;

describe('CampaignCustomerAllocateModalComponent', () => {
  const api = jasmine.createSpyObj<CampaignCustomerService>('CampaignCustomerService', [
    'allocate',
  ]);

  const open = async (unassigned: number) => {
    const fixture = TestBed.createComponent(CampaignCustomerAllocateModalComponent);
    fixture.componentRef.setInput('campaign', campaign);
    fixture.componentRef.setInput('unassigned', unassigned);
    fixture.componentRef.setInput('holding', [
      { employeeId: 'e1', employeeCode: 'E1', employeeName: 'Thảo', assigned: 12, closed: 4 },
    ]);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  };

  beforeEach(async () => {
    api.allocate.calls.reset();
    api.allocate.and.returnValue(
      of({
        assignedCount: 6,
        byEmployee: [{ employeeId: 'e1', employeeCode: 'E1', employeeName: 'Thảo', count: 3 }],
      }),
    );
    await TestBed.configureTestingModule({
      imports: [CampaignCustomerAllocateModalComponent],
      providers: [{ provide: CampaignCustomerService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('selects every participant and shows what each already holds', async () => {
    const fixture = await open(6);
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelectorAll<HTMLInputElement>('.participants input:checked').length).toBe(2);
    expect(root.querySelector('.participants')?.textContent).toContain('12 khách đang giữ');
    expect(root.querySelector('.modal-footer .button-primary')?.textContent).toContain(
      'Phân bổ 6 khách',
    );
  });

  it('allocates with the chosen rule and shows the result per employee', async () => {
    const fixture = await open(6);
    const root: HTMLElement = fixture.nativeElement;

    root.querySelectorAll<HTMLInputElement>('input[name="allocation-rule"]')[2].click();
    root.querySelectorAll<HTMLInputElement>('.participants input')[1].click();
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('.modal-footer .button-primary')!.click();
    fixture.detectChanges();

    expect(api.allocate).toHaveBeenCalledWith('c1', { rule: 'EQUAL_SPLIT', employeeIds: ['e1'] });
    expect(root.querySelector('.result-list')?.textContent).toContain('Thảo');
    expect(root.querySelector('.count-line')?.textContent).toContain('Đã phân bổ 6 khách');
  });

  it('disables the button when nothing is unassigned', async () => {
    const fixture = await open(0);
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelector<HTMLButtonElement>('.modal-footer .button-primary')!.disabled).toBe(
      true,
    );
    expect(root.textContent).toContain('Không còn khách chưa phân bổ');
  });

  it('blocks a request over the 2000 customer limit', async () => {
    const fixture = await open(2500);
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelector<HTMLButtonElement>('.modal-footer .button-primary')!.disabled).toBe(
      true,
    );
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('2000');
  });
});
