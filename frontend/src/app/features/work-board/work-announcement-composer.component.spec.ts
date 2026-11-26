import { HttpErrorResponse } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { CampaignListOverview } from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { NotificationRecipientOption } from '../../core/notification.models';
import { NotificationService } from '../../core/notification.service';
import { WorkListItem } from '../../core/work.models';
import { WorkService } from '../../core/work.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { WorkAnnouncementComposerComponent } from './work-announcement-composer.component';

@Component({ selector: 'app-header', standalone: true, template: '' })
class HeaderStub {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class SidebarStub {}

const RECIPIENTS: NotificationRecipientOption[] = [
  {
    id: 'emp-1',
    employeeCode: 'NV001',
    fullName: 'Nguyễn Văn A',
    departmentId: 'dept-1',
    departmentName: 'Phòng Bán lẻ',
    jobPositionTitle: 'Giao dịch viên',
  },
  {
    id: 'emp-2',
    employeeCode: 'NV002',
    fullName: 'Trần Thị B',
    departmentId: 'dept-1',
    departmentName: 'Phòng Bán lẻ',
    jobPositionTitle: 'Chuyên viên QHKH',
  },
  {
    id: 'emp-3',
    employeeCode: 'NV003',
    fullName: 'Lê Văn C',
    departmentId: 'dept-1',
    departmentName: 'Phòng Bán lẻ',
    jobPositionTitle: 'Giao dịch viên',
  },
];

const SAMPLE_WORKS: WorkListItem[] = [
  {
    id: 'w-1',
    code: 'WK-1042',
    title: 'Mở thẻ tín dụng Q4',
    workTypeId: 'wt-1',
    workTypeName: 'Mở thẻ tín dụng',
    businessCampaignId: null,
    assigneeEmployeeId: 'emp-1',
    assigneeEmployeeCode: 'NV001',
    assigneeEmployeeName: 'Nguyễn Văn A',
    departmentId: 'dept-1',
    departmentCode: 'BL1',
    departmentName: 'Phòng Bán lẻ',
    priority: 'High',
    status: 'InProgress',
    plannedStartDate: '2026-11-01',
    plannedEndDate: '2026-11-30',
    updatedAt: '2026-11-20T08:00:00Z',
  },
];

const SAMPLE_CAMPAIGNS: CampaignListOverview = {
  metrics: {
    totalCampaigns: 1,
    runningCampaigns: 1,
    plannedCampaigns: 0,
    completedCampaigns: 0,
    totalTasksGenerated: 10,
    totalCompletedTasks: 4,
    slowCampaignCount: 0,
  },
  campaigns: {
    items: [
      {
        id: 'cmp-1',
        code: 'CMP-2026-07',
        name: 'Chiến dịch Thẻ tín dụng Q4',
        description: null,
        startDate: '2026-10-01',
        endDate: '2026-12-31',
        status: 'RUNNING',
        businessObjectiveId: 'obj-1',
        businessObjectiveCode: 'OBJ-1',
        businessObjectiveName: 'Tăng trưởng thẻ',
        participantsCount: 8,
        workTypesCount: 2,
        generatedWorksCount: 10,
        completedWorksCount: 4,
        updatedAt: '2026-11-20T08:00:00Z',
        progressPercent: 40,
        timeElapsedPercent: 50,
        paceGap: -10,
        kpiCode: null,
        kpiName: null,
        targetUnit: null,
        targetValue: null,
        actualValue: null,
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 50,
  },
};

describe('WorkAnnouncementComposerComponent', () => {
  let notifications: jasmine.SpyObj<NotificationService>;
  let works: jasmine.SpyObj<WorkService>;
  let campaigns: jasmine.SpyObj<CampaignService>;
  let i18n: I18nService;

  const root = (fixture: ComponentFixture<WorkAnnouncementComposerComponent>) =>
    fixture.nativeElement as HTMLElement;
  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  async function configure(queryParams: Record<string, string> = {}): Promise<void> {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [WorkAnnouncementComposerComponent],
      providers: [
        provideRouter([]),
        { provide: NotificationService, useValue: notifications },
        { provide: WorkService, useValue: works },
        { provide: CampaignService, useValue: campaigns },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
        },
        {
          provide: AuthService,
          useValue: {
            currentUser: signal({
              id: 'u-mgr',
              email: 'mgr@bank.local',
              fullName: 'Vũ Thị Lan',
              roles: ['DEPT_MANAGER'],
              permissions: ['notification:send'],
            }),
          },
        },
      ],
    })
      .overrideComponent(WorkAnnouncementComposerComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');
  }

  function create(): ComponentFixture<WorkAnnouncementComposerComponent> {
    const fixture = TestBed.createComponent(WorkAnnouncementComposerComponent);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(async () => {
    notifications = jasmine.createSpyObj<NotificationService>('NotificationService', [
      'recipients',
      'send',
    ]);
    notifications.recipients.and.returnValue(of(RECIPIENTS));
    notifications.send.and.returnValue(of(3));

    works = jasmine.createSpyObj<WorkService>('WorkService', ['list']);
    works.list.and.returnValue(of({ items: SAMPLE_WORKS, totalCount: 1, page: 1, pageSize: 50 }));

    campaigns = jasmine.createSpyObj<CampaignService>('CampaignService', ['list']);
    campaigns.list.and.returnValue(of(SAMPLE_CAMPAIGNS));

    await configure();
  });

  it('Composer_DefaultScope_LoadsRecipientsUpdatesLivePreviewAndSendsAnnouncement', () => {
    const fixture = create();
    const el = root(fixture);

    expect(notifications.recipients).toHaveBeenCalledTimes(1);
    expect(text(el.querySelector('#announcement-target-all'))).toContain('3');
    expect(el.querySelector<HTMLButtonElement>('#announcement-submit')?.disabled).toBeTrue();

    const titleInput = el.querySelector<HTMLInputElement>('#announcement-title')!;
    titleInput.value = 'Họp giao ban đầu tuần';
    titleInput.dispatchEvent(new Event('input'));

    const bodyInput = el.querySelector<HTMLTextAreaElement>('#announcement-body')!;
    bodyInput.value = 'Phòng họp tầng 3 lúc 8h30.';
    bodyInput.dispatchEvent(new Event('input'));

    const sevSelect = el.querySelector<HTMLSelectElement>('#announcement-severity')!;
    sevSelect.value = 'IMPORTANT';
    sevSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(text(el.querySelector('.preview-title'))).toBe('Họp giao ban đầu tuần');
    expect(text(el.querySelector('.preview-meta'))).toContain('Vũ Thị Lan');
    expect(el.querySelector('.preview-box')?.classList).toContain('is-important');

    el.querySelector<HTMLButtonElement>('#announcement-submit')!.click();
    fixture.detectChanges();

    expect(notifications.send).toHaveBeenCalledOnceWith({
      title: 'Họp giao ban đầu tuần',
      body: 'Phòng họp tầng 3 lúc 8h30.',
      severity: 'IMPORTANT',
    });
    expect(text(el.querySelector('#announcement-success'))).toContain('3');
    expect(el.querySelectorAll('.recent-item').length).toBe(1);
    expect(titleInput.value).toBe('');
  });

  it('Composer_ByPositionMode_GroupsPositionsAndMapsSelectedPositionToEmployeeIds', () => {
    notifications.send.and.returnValue(of(2));
    const fixture = create();
    const el = root(fixture);

    el.querySelector<HTMLButtonElement>('#announcement-target-position')!.click();
    fixture.detectChanges();

    // Two distinct positions: Chuyên viên QHKH (1), Giao dịch viên (2)
    const checkboxes = Array.from(
      el.querySelectorAll<HTMLInputElement>('.picker-list input[type="checkbox"]'),
    );
    expect(checkboxes.length).toBe(2);

    // Select "Giao dịch viên" (index 1 in alphabetical vi order)
    checkboxes[1].click();
    fixture.detectChanges();
    expect(text(el.querySelector('.picker-summary'))).toContain('1');
    expect(text(el.querySelector('.picker-summary'))).toContain('2');

    const titleInput = el.querySelector<HTMLInputElement>('#announcement-title')!;
    titleInput.value = 'Nhắc kiểm quỹ cuối ngày';
    titleInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('#announcement-submit')!.click();
    fixture.detectChanges();

    expect(notifications.send).toHaveBeenCalledOnceWith(
      jasmine.objectContaining({
        title: 'Nhắc kiểm quỹ cuối ngày',
        recipientEmployeeIds: ['emp-1', 'emp-3'],
      }),
    );
  });

  it('Composer_PickEmployeesMode_FiltersAndSelectsSpecificEmployees', () => {
    notifications.send.and.returnValue(of(1));
    const fixture = create();
    const el = root(fixture);

    el.querySelector<HTMLButtonElement>('#announcement-target-employees')!.click();
    fixture.detectChanges();

    const search = el.querySelector<HTMLInputElement>('#announcement-employee-search')!;
    search.value = 'NV002';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const checkboxes = Array.from(
      el.querySelectorAll<HTMLInputElement>('.picker-list input[type="checkbox"]'),
    );
    expect(checkboxes.length).toBe(1);
    checkboxes[0].click();
    fixture.detectChanges();

    const titleInput = el.querySelector<HTMLInputElement>('#announcement-title')!;
    titleInput.value = 'Trao đổi hồ sơ KH';
    titleInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('#announcement-submit')!.click();
    fixture.detectChanges();

    expect(notifications.send).toHaveBeenCalledOnceWith(
      jasmine.objectContaining({
        title: 'Trao đổi hồ sơ KH',
        recipientEmployeeIds: ['emp-2'],
      }),
    );
  });

  it('Composer_RelatedCampaignAndWork_RequiresRecordWhenSelectedAndSendsLink', () => {
    const fixture = create();
    const el = root(fixture);

    const titleInput = el.querySelector<HTMLInputElement>('#announcement-title')!;
    titleInput.value = 'Triển khai chiến dịch Q4';
    titleInput.dispatchEvent(new Event('input'));

    const relType = el.querySelector<HTMLSelectElement>('#announcement-related-type')!;
    relType.value = 'CAMPAIGN';
    relType.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(campaigns.list).toHaveBeenCalledWith({ page: 1, pageSize: 50 });
    expect(el.querySelector<HTMLButtonElement>('#announcement-submit')?.disabled).toBeTrue();

    const relId = el.querySelector<HTMLSelectElement>('#announcement-related-id')!;
    relId.value = 'cmp-1';
    relId.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(text(el.querySelector('.preview-link-tag'))).toContain('CMP-2026-07');

    // Submit using Ctrl+Enter keyboard shortcut
    el.querySelector('form')!.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, bubbles: true }),
    );
    fixture.detectChanges();

    expect(notifications.send).toHaveBeenCalledOnceWith({
      title: 'Triển khai chiến dịch Q4',
      body: undefined,
      severity: 'NORMAL',
      relatedType: 'CAMPAIGN',
      relatedId: 'cmp-1',
    });
  });

  it('Composer_SendErrorsAndRecipientRetry_ShowsAlertsAndRecovers', () => {
    notifications.recipients.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    const fixture = create();
    const el = root(fixture);

    expect(el.querySelector('#announcement-recipients-retry')).toBeTruthy();

    notifications.recipients.and.returnValue(of(RECIPIENTS));
    el.querySelector<HTMLButtonElement>('#announcement-recipients-retry')!.click();
    fixture.detectChanges();
    expect(el.querySelector('#announcement-recipients-retry')).toBeNull();

    notifications.send.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
    const titleInput = el.querySelector<HTMLInputElement>('#announcement-title')!;
    titleInput.value = 'Kiểm tra phân quyền';
    titleInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('#announcement-submit')!.click();
    fixture.detectChanges();

    expect(text(el.querySelector('#announcement-error'))).toContain('không có quyền');
  });

  it('Composer_CancelAndLanguageSwitch_NavigatesBackAndSwitchesToEnglish', () => {
    const router = TestBed.inject(Router);
    const navSpy = spyOn(router, 'navigate').and.resolveTo(true);
    const fixture = create();
    const el = root(fixture);

    i18n.setLanguage('en');
    fixture.detectChanges();
    expect(text(el.querySelector('h1'))).toBe('Compose team announcement');

    el.querySelector<HTMLButtonElement>('#announcement-cancel')!.click();
    expect(navSpy).toHaveBeenCalledOnceWith(['/my-work'], { queryParams: { view: 'monitor' } });
  });

  it('Composer_QueryParams_PreloadsLinkedWork', async () => {
    await configure({ relatedType: 'WORK', relatedId: 'w-1' });
    const fixture = create();
    const el = root(fixture);

    expect(works.list).toHaveBeenCalledWith({ page: 1, pageSize: 50 });
    expect(text(el.querySelector('.preview-link-tag'))).toContain('WK-1042');
  });
});
