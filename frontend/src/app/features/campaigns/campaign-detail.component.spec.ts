import { Component, Input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { CampaignDetail } from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { CampaignBatchModalComponent } from './campaign-batch-modal.component';
import { CampaignDetailComponent } from './campaign-detail.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}
@Component({ selector: 'app-campaign-batch-modal', template: '' })
class BatchModalStub {
  @Input() campaign?: CampaignDetail;
}

const detail = (overrides: Partial<CampaignDetail> = {}): CampaignDetail => ({
  id: 'c1',
  code: 'CMP-2026-07',
  name: 'Đẩy mạnh thẻ tín dụng',
  description: 'Mô tả chiến dịch',
  startDate: '2026-10-01',
  endDate: '2099-11-30',
  status: 'RUNNING',
  businessObjective: { id: 'o1', code: 'OBJ-2026-03', name: 'Tăng trưởng', description: null },
  workTypes: [
    {
      workTypeId: 'w1',
      code: 'WT-S01',
      name: 'Bán thẻ',
      process: null,
      expectedResult: null,
      requiredEvidence: null,
      targetUnit: 'tài khoản',
    },
  ],
  participants: [
    {
      employeeId: 'e1',
      employeeCode: 'NV-1',
      fullName: 'Phạm Thu Hà',
      jobPositionTitle: 'Sales Officer',
      departmentName: 'KHCN',
      assignedTasksCount: 9,
      completedTasksCount: 4,
    },
  ],
  metrics: {
    totalWorksCount: 96,
    completedWorksCount: 41,
    inExecutionWorksCount: 30,
    pendingWorksCount: 25,
    progressPercent: 42.7,
  },
  canGenerateBatchWork: true,
  canApprove: false,
  updatedAt: '2026-10-20T10:00:00Z',
  ...overrides,
});

describe('CampaignDetailComponent', () => {
  const api = { detail: jasmine.createSpy('detail') };

  const open = async () => {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/campaigns/c1', CampaignDetailComponent);
    harness.detectChanges();
    return { harness, component, root: harness.routeNativeElement as HTMLElement };
  };

  beforeEach(async () => {
    api.detail.calls.reset();
    api.detail.and.returnValue(of(detail()));
    await TestBed.configureTestingModule({
      imports: [CampaignDetailComponent],
      providers: [
        provideRouter([{ path: 'campaigns/:id', component: CampaignDetailComponent }]),
        { provide: CampaignService, useValue: api },
      ],
    })
      .overrideComponent(CampaignDetailComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent, CampaignBatchModalComponent] },
        add: { imports: [HeaderStub, SidebarStub, BatchModalStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('loads the campaign named by the route and shows its overview', async () => {
    const { root } = await open();

    expect(api.detail).toHaveBeenCalledWith('c1');
    expect(root.querySelector('h1')?.textContent).toContain('CMP-2026-07 – Đẩy mạnh thẻ tín dụng');
    expect(root.textContent).toContain('42.7%');
    expect(root.textContent).toContain('96 / 41');
    expect(root.textContent).toContain('OBJ-2026-03 · Tăng trưởng');
    expect(root.textContent).toContain('WT-S01');
  });

  it('switches to the participants tab', async () => {
    const { harness, root } = await open();

    root.querySelectorAll<HTMLButtonElement>('[role="tab"]')[1].click();
    harness.detectChanges();

    expect(root.querySelector('tbody')?.textContent).toContain('Phạm Thu Hà');
  });

  it('opens the batch work modal from the toolbar button', async () => {
    const { harness, root } = await open();

    root.querySelector<HTMLButtonElement>('.page-toolbar .button-primary')!.click();
    harness.detectChanges();

    expect(root.querySelector('app-campaign-batch-modal')).not.toBeNull();
  });

  it('hides the batch work button when the campaign does not allow it', async () => {
    api.detail.and.returnValue(of(detail({ canGenerateBatchWork: false })));
    const { root } = await open();

    expect(root.querySelector('.page-toolbar .button-primary')).toBeNull();
  });

  it('shows the result and reloads after batch work is generated', async () => {
    const { harness, component, root } = await open();

    component['onGenerated']({
      campaignId: 'c1',
      campaignCode: 'CMP-2026-07',
      totalGenerated: 36,
      participantCount: 3,
      message: 'Đã tạo 36 công việc',
    });
    harness.detectChanges();

    expect(api.detail).toHaveBeenCalledTimes(2);
    expect(root.querySelector('.notice')?.textContent).toContain('Đã tạo 36 công việc');
  });

  it('shows a not-found state when the campaign cannot be loaded', async () => {
    api.detail.and.returnValue(throwError(() => new Error('404')));
    const { root } = await open();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'Không tìm thấy chiến dịch',
    );
  });

  it('marks a campaign past its end date as overdue', async () => {
    api.detail.and.returnValue(of(detail({ endDate: '2020-01-01' })));
    const { root } = await open();

    expect(root.querySelector('.is-overdue')?.textContent).toContain('Đã quá hạn');
  });
});
