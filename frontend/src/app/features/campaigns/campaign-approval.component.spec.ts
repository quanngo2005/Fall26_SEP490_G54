import { HttpErrorResponse } from '@angular/common/http';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { CampaignDetail, CampaignSummary } from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { CampaignApprovalComponent } from './campaign-approval.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const summary = (id: string, code: string): CampaignSummary => ({
  id,
  code,
  name: `Plan ${code}`,
  description: null,
  startDate: '2099-01-01',
  endDate: '2099-02-01',
  status: 'PLANNED',
  businessObjectiveId: 'o1',
  businessObjectiveCode: 'OBJ-1',
  businessObjectiveName: 'Objective',
  participantsCount: 2,
  workTypesCount: 1,
  generatedWorksCount: 0,
  completedWorksCount: 0,
  updatedAt: '2026-10-20T10:00:00Z',
  progressPercent: 0,
  timeElapsedPercent: 0,
  paceGap: 0,
  kpiCode: null,
  kpiName: null,
  targetUnit: null,
  targetValue: null,
  actualValue: null,
});

const detail = (id: string, overrides: Partial<CampaignDetail> = {}): CampaignDetail => ({
  id,
  code: id === 'c1' ? 'CMP-1' : 'CMP-2',
  name: `Plan ${id}`,
  description: null,
  startDate: '2099-01-01',
  endDate: '2099-02-01',
  status: 'PLANNED',
  businessObjective: { id: 'o1', code: 'OBJ-1', name: 'Objective', description: null },
  workTypes: [
    {
      workTypeId: 'w1',
      code: 'WT-S01',
      name: 'Bán thẻ',
      process: null,
      expectedResult: null,
      requiredEvidence: null,
      targetUnit: null,
    },
  ],
  participants: [],
  metrics: {
    totalWorksCount: 0,
    completedWorksCount: 0,
    inExecutionWorksCount: 0,
    pendingWorksCount: 0,
    progressPercent: 0,
  },
  canGenerateBatchWork: false,
  canApprove: true,
  updatedAt: '2026-10-20T10:00:00Z',
  ...overrides,
});

const overview = (items: CampaignSummary[]) => ({
  metrics: {
    totalCampaigns: items.length,
    runningCampaigns: 0,
    plannedCampaigns: items.length,
    completedCampaigns: 0,
    totalTasksGenerated: 0,
    totalCompletedTasks: 0,
  },
  campaigns: { items, totalCount: items.length, page: 1, pageSize: 50 },
});

describe('CampaignApprovalComponent', () => {
  const api = {
    list: jasmine.createSpy('list'),
    detail: jasmine.createSpy('detail'),
    decide: jasmine.createSpy('decide'),
  };

  const open = async (url = '/campaigns/approval') => {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, CampaignApprovalComponent);
    harness.detectChanges();
    return { harness, component, root: harness.routeNativeElement as HTMLElement };
  };

  const button = (root: HTMLElement, text: string) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      b.textContent?.includes(text),
    );

  beforeEach(async () => {
    api.list.calls.reset();
    api.detail.calls.reset();
    api.decide.calls.reset();
    api.list.and.returnValue(of(overview([summary('c1', 'CMP-1'), summary('c2', 'CMP-2')])));
    api.detail.and.callFake((id: string) => of(detail(id)));
    await TestBed.configureTestingModule({
      imports: [CampaignApprovalComponent],
      providers: [
        provideRouter([{ path: 'campaigns/approval', component: CampaignApprovalComponent }]),
        { provide: CampaignService, useValue: api },
      ],
    })
      .overrideComponent(CampaignApprovalComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('loads planned campaigns and reviews the first one', async () => {
    const { root } = await open();

    expect(api.list).toHaveBeenCalledWith(jasmine.objectContaining({ status: 'PLANNED' }));
    expect(root.querySelectorAll('.queue-item').length).toBe(2);
    expect(api.detail).toHaveBeenCalledWith('c1');
    expect(root.textContent).toContain('WT-S01');
  });

  it('preselects the campaign from the id query parameter', async () => {
    await open('/campaigns/approval?id=c2');

    expect(api.detail).toHaveBeenCalledWith('c2');
  });

  it('approves without a note and reloads the queue', async () => {
    api.decide.and.returnValue(
      of({ campaignId: 'c1', status: 'RUNNING', decidedAt: '', message: 'Đã phê duyệt' }),
    );
    const { harness, root } = await open();
    api.list.and.returnValue(of(overview([summary('c2', 'CMP-2')])));

    button(root, 'Duyệt kế hoạch')?.click();
    harness.detectChanges();

    expect(api.decide).toHaveBeenCalledWith('c1', { decision: 'APPROVE', note: null });
    expect(root.textContent).toContain('Đã phê duyệt');
    expect(root.querySelectorAll('.queue-item').length).toBe(1);
  });

  it('requires a note to reject', async () => {
    api.decide.and.returnValue(
      of({ campaignId: 'c1', status: 'CANCELLED', decidedAt: '', message: 'Đã từ chối' }),
    );
    const { harness, component, root } = await open();
    const reject = () => button(root, 'Từ chối') as HTMLButtonElement;

    expect(reject().disabled).toBeTrue();

    (component as unknown as { note: string }).note = 'Thiếu chỉ tiêu';
    harness.detectChanges();
    expect(reject().disabled).toBeFalse();

    reject().click();
    expect(api.decide).toHaveBeenCalledWith('c1', { decision: 'REJECT', note: 'Thiếu chỉ tiêu' });
  });

  it('disables the decision when the user cannot approve', async () => {
    api.detail.and.callFake((id: string) => of(detail(id, { canApprove: false })));
    const { root } = await open();

    expect((button(root, 'Duyệt kế hoạch') as HTMLButtonElement).disabled).toBeTrue();
    expect(root.querySelector('.hint')).not.toBeNull();
  });

  it('shows the server message when the decision fails', async () => {
    api.decide.and.returnValue(
      throwError(() => new HttpErrorResponse({ error: { message: 'Maker không được duyệt' } })),
    );
    const { harness, root } = await open();

    button(root, 'Duyệt kế hoạch')?.click();
    harness.detectChanges();

    expect(root.querySelector('.notice.is-error')?.textContent).toContain('Maker không được duyệt');
    expect(root.querySelectorAll('.queue-item').length).toBe(2);
  });

  it('shows an empty state when nothing is pending', async () => {
    api.list.and.returnValue(of(overview([])));
    const { root } = await open();

    expect(root.querySelector('.queue-item')).toBeNull();
    expect(root.textContent).toContain('Không có kế hoạch nào đang chờ duyệt');
  });

  it('shows an error state with retry when loading fails', async () => {
    api.list.and.returnValue(throwError(() => new Error('boom')));
    const { harness, root } = await open();

    expect(root.querySelector('.state-error')).not.toBeNull();
    api.list.and.returnValue(of(overview([summary('c1', 'CMP-1')])));
    button(root, 'Thử lại')?.click();
    harness.detectChanges();

    expect(root.querySelectorAll('.queue-item').length).toBe(1);
  });
});
