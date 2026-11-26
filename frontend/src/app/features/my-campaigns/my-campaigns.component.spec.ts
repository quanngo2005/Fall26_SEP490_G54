import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { MyCampaign } from '../../core/campaign-customer.models';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { MyCampaignsComponent } from './my-campaigns.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const campaign = (over: Partial<MyCampaign>): MyCampaign => ({
  campaignId: 'c1',
  campaignCode: 'CMP-2026-06',
  campaignName: 'Tiền gửi cuối năm',
  status: 'RUNNING',
  startDate: '2026-10-01',
  endDate: '2026-12-31',
  totalAssigned: 15,
  closed: 1,
  byStatus: [
    { status: 'NOT_CONTACTED', count: 9 },
    { status: 'CLOSED', count: 1 },
  ],
  ...over,
});

describe('MyCampaignsComponent', () => {
  const api = jasmine.createSpyObj<CampaignCustomerService>('CampaignCustomerService', [
    'myCampaigns',
  ]);

  const open = async (items: MyCampaign[]) => {
    api.myCampaigns.and.returnValue(of(items));
    const fixture = TestBed.createComponent(MyCampaignsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyCampaignsComponent],
      providers: [provideRouter([]), { provide: CampaignCustomerService, useValue: api }],
    })
      .overrideComponent(MyCampaignsComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('shows my progress with units and flags campaigns with customers not contacted', async () => {
    const { root } = await open([campaign({})]);

    expect(root.textContent).toContain('1/15 đã chốt (7%)');
    expect(root.querySelector('.campaign-card.needs-action')).not.toBeNull();
    expect(root.textContent).toContain('9 khách chưa liên hệ');
  });

  it('keeps ended campaigns on their own tab', async () => {
    const { fixture, root } = await open([
      campaign({}),
      campaign({ campaignId: 'c2', campaignName: 'Đã xong', status: 'COMPLETED' }),
    ]);

    expect(root.textContent).not.toContain('Đã xong');
    root.querySelectorAll<HTMLButtonElement>('[role="tab"]')[1].click();
    fixture.detectChanges();

    expect(root.textContent).toContain('Đã xong');
    expect(root.querySelector('.needs-action')).toBeNull();
  });

  it('shows an empty state when nothing is assigned', async () => {
    const { root } = await open([]);

    expect(root.textContent).toContain('chưa được giao khách hàng');
  });
});
