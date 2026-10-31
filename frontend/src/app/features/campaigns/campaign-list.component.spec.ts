import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { CampaignListOverview, CampaignSummary } from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { CampaignCreateModalComponent } from './campaign-create-modal.component';
import { CampaignListComponent } from './campaign-list.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}
@Component({ selector: 'app-campaign-create-modal', template: '' })
class CreateModalStub {}

const campaign = (overrides: Partial<CampaignSummary> = {}): CampaignSummary => ({
  id: 'c1',
  code: 'CMP-2026-06',
  name: 'Tiền gửi cuối năm',
  description: null,
  startDate: '2026-10-01',
  endDate: '2026-12-31',
  status: 'RUNNING',
  businessObjectiveId: 'o1',
  businessObjectiveCode: 'OBJ-2026-03',
  businessObjectiveName: 'Tăng trưởng',
  participantsCount: 12,
  workTypesCount: 2,
  generatedWorksCount: 36,
  completedWorksCount: 9,
  updatedAt: '2026-10-20T10:00:00Z',
  ...overrides,
});

const overview = (items: CampaignSummary[], totalCount = items.length): CampaignListOverview => ({
  metrics: {
    totalCampaigns: 7,
    runningCampaigns: 2,
    plannedCampaigns: 1,
    completedCampaigns: 2,
    totalTasksGenerated: 54,
    totalCompletedTasks: 20,
  },
  campaigns: { items, totalCount, page: 1, pageSize: 10 },
});

describe('CampaignListComponent', () => {
  const api = {
    list: jasmine.createSpy('list'),
    objectives: jasmine.createSpy('objectives'),
  };

  const create = async () => {
    const fixture = TestBed.createComponent(CampaignListComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    Object.values(api).forEach((spy) => spy.calls.reset());
    api.list.and.returnValue(
      of(
        overview(
          [
            campaign(),
            campaign({ id: 'c2', code: 'CMP-2026-07', status: 'PLANNED', generatedWorksCount: 0 }),
          ],
          12,
        ),
      ),
    );
    api.objectives.and.returnValue(
      of([{ id: 'o1', code: 'OBJ-2026-03', name: 'Tăng trưởng', description: null }]),
    );
    await TestBed.configureTestingModule({
      imports: [CampaignListComponent],
      providers: [provideRouter([]), { provide: CampaignService, useValue: api }],
    })
      .overrideComponent(CampaignListComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent, CampaignCreateModalComponent] },
        add: { imports: [HeaderStub, SidebarStub, CreateModalStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('loads the first page and shows the status counts, rows and progress', async () => {
    const fixture = await create();
    const root: HTMLElement = fixture.nativeElement;

    expect(api.list).toHaveBeenCalledWith({
      search: '',
      status: '',
      objectiveId: '',
      page: 1,
      pageSize: 10,
    });
    expect(root.textContent).toContain('Tất cả 7');
    expect(root.textContent).toContain('Chờ duyệt 1');
    expect(root.textContent).toContain('CMP-2026-06');
    expect(root.textContent).toContain('25%');
    expect(root.textContent).toContain('1–10 / 12 chiến dịch');
    expect(root.querySelectorAll('tbody tr').length).toBe(2);
  });

  it('filters by the selected status tab and returns to page 1', async () => {
    const fixture = await create();
    const root: HTMLElement = fixture.nativeElement;
    const next = root.querySelectorAll<HTMLButtonElement>('.pager-buttons button')[1];
    next.click();
    fixture.detectChanges();
    expect(api.list.calls.mostRecent().args[0].page).toBe(2);

    root.querySelectorAll<HTMLButtonElement>('[role="tab"]')[2].click();
    fixture.detectChanges();

    expect(api.list.calls.mostRecent().args[0]).toEqual(
      jasmine.objectContaining({ status: 'RUNNING', page: 1 }),
    );
  });

  it('opens the campaign detail when a row is clicked', async () => {
    const fixture = await create();
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    fixture.nativeElement.querySelector('tbody tr').click();

    expect(navigate).toHaveBeenCalledWith(['/campaigns', 'c1']);
  });

  it('shows an empty state when nothing matches', async () => {
    api.list.and.returnValue(of(overview([])));
    const fixture = await create();

    expect(fixture.nativeElement.textContent).toContain('Không có chiến dịch phù hợp');
  });

  it('shows a load error with retry', async () => {
    api.list.and.returnValue(throwError(() => new Error('500')));
    const fixture = await create();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Không tải được');

    api.list.and.returnValue(of(overview([campaign()])));
    root.querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(root.textContent).toContain('CMP-2026-06');
  });

  it('opens the plan modal and goes to the new campaign once it is created', async () => {
    const fixture = await create();
    const root: HTMLElement = fixture.nativeElement;
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    root.querySelector<HTMLButtonElement>('.page-toolbar .button-primary')!.click();
    fixture.detectChanges();
    expect(root.querySelector('app-campaign-create-modal')).not.toBeNull();

    fixture.componentInstance['onCreated']('c9');
    fixture.detectChanges();

    expect(navigate).toHaveBeenCalledWith(['/campaigns', 'c9']);
    expect(root.querySelector('app-campaign-create-modal')).toBeNull();
  });
});
