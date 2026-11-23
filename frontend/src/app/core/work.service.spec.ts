import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { WorkDetail, WorkListItem } from './work.models';
import { WorkService } from './work.service';

describe('WorkService', () => {
  let service: WorkService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/works`;
  const item: WorkListItem = {
    id: 'w1',
    code: 'WRK-20261022-1234',
    title: 'Mở tài khoản khách hàng A',
    workTypeId: 't1',
    workTypeName: 'Mở tài khoản',
    businessCampaignId: null,
    assigneeEmployeeId: 'e1',
    assigneeEmployeeCode: 'NV004',
    assigneeEmployeeName: 'Lê Minh Châu',
    departmentId: 'd1',
    departmentCode: 'CN-HN',
    departmentName: 'Kinh doanh 1',
    priority: 'Medium',
    status: 'InProgress',
    plannedStartDate: '2026-10-20',
    plannedEndDate: '2026-10-30',
    updatedAt: '2026-10-22T08:00:00+00:00',
  };
  const detail: WorkDetail = {
    id: 'w1',
    code: item.code,
    title: item.title,
    description: null,
    workTypeId: 't1',
    workTypeName: 'Mở tài khoản',
    businessCampaignId: null,
    businessCampaignName: null,
    assigneeEmployeeId: 'e1',
    assigneeEmployeeName: 'Lê Minh Châu',
    assigneeEmployeeCode: 'NV004',
    priority: 'Medium',
    status: 'InProgress',
    plannedStartDate: '2026-10-20',
    plannedEndDate: '2026-10-30',
    estimatedHours: null,
    updatedAt: '2026-10-22T09:00:00+00:00',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(WorkService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends filters as query parameters, joins statuses with commas and skips blanks', () => {
    service
      .list({
        keyword: 'mở',
        status: ['InProgress', 'Rejected'],
        assigneeEmployeeId: 'e1',
        workTypeId: '',
        priority: 'High',
        page: 2,
        pageSize: 50,
      })
      .subscribe((page) => {
        expect(page.items[0].status).toBe('InProgress');
        expect(page.totalCount).toBe(1);
      });
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('keyword')).toBe('mở');
    expect(request.request.params.get('status')).toBe('InProgress,Rejected');
    expect(request.request.params.get('assigneeEmployeeId')).toBe('e1');
    expect(request.request.params.get('priority')).toBe('High');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('50');
    expect(request.request.params.has('workTypeId')).toBeFalse();
    request.flush({
      success: true,
      status: 200,
      data: { items: [item], totalCount: 1, page: 2, pageSize: 50 },
    });
  });

  it('omits the status parameter when no status is selected', () => {
    service.list({ status: [] }).subscribe();
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.params.has('status')).toBeFalse();
    request.flush({
      success: true,
      status: 200,
      data: { items: [], totalCount: 0, page: 1, pageSize: 20 },
    });
  });

  it('loads one work item by id', () => {
    service.detail('w 1').subscribe((work) => expect(work.code).toBe(item.code));
    const request = http.expectOne(`${url}/w%201`);
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, status: 200, data: detail });
  });

  it('patches the status and returns the updated work', () => {
    service
      .changeStatus('w1', { status: 'InProgress' })
      .subscribe((work) => expect(work.updatedAt).toBe(detail.updatedAt));
    const request = http.expectOne(`${url}/w1/status`);
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ status: 'InProgress' });
    request.flush({ success: true, status: 200, data: detail });
  });

  it('rejects an unsuccessful envelope', () => {
    let failed = false;
    service.detail('w1').subscribe({ error: () => (failed = true) });
    http.expectOne(`${url}/w1`).flush({ success: false, status: 200, data: null, message: 'x' });
    expect(failed).toBeTrue();
  });

  it('creates a work through POST /works', () => {
    let code = '';
    service
      .create({
        title: 'Mở thẻ tín dụng',
        workTypeId: 't1',
        assigneeEmployeeId: 'e1',
        priority: 'High',
        plannedEndDate: '2026-11-30',
      })
      .subscribe((work) => (code = work.code));

    const request = http.expectOne(url);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.priority).toBe('High');
    request.flush({ success: true, status: 200, data: { ...item, code: 'WRK-1' } });

    expect(code).toBe('WRK-1');
  });

  it('reads the activity log of a work', () => {
    let messages: string[] = [];
    service.activities('w1').subscribe((list) => (messages = list.map((a) => a.message)));

    const request = http.expectOne(`${url}/w1/activities`);
    expect(request.request.method).toBe('GET');
    request.flush({
      success: true,
      status: 200,
      data: [
        {
          id: 'a1',
          action: 'ASSIGNED',
          message: 'Việc được giao',
          actorEmployeeId: 'e9',
          actorName: 'Lê Văn C',
          createdAt: '2026-10-24T02:10:00+00:00',
        },
      ],
    });

    expect(messages).toEqual(['Việc được giao']);
  });

  it('ticks a checklist item through PATCH and returns the updated work', () => {
    let progress: number | null | undefined;
    service
      .setChecklistItem('w1', 'c1', true)
      .subscribe((work) => (progress = work.progressPercent));

    const request = http.expectOne(`${url}/w1/checklist-items/c1`);
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ isDone: true });
    request.flush({ success: true, status: 200, data: { ...detail, progressPercent: 50 } });

    expect(progress).toBe(50);
  });

  it('saves the plan through PUT /works/{id}/plan', () => {
    let deadline = '';
    service
      .updatePlan('w1', {
        plannedEndDate: '2026-10-27',
        priority: 'High',
        checklist: [{ id: 'c1', title: 'Xác minh giấy tờ' }, { title: 'Gửi email' }],
        note: 'Khách hẹn lại',
      })
      .subscribe((work) => (deadline = work.plannedEndDate ?? ''));

    const request = http.expectOne(`${url}/w1/plan`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.checklist.length).toBe(2);
    expect(request.request.body.note).toBe('Khách hẹn lại');
    request.flush({
      success: true,
      status: 200,
      data: { ...detail, plannedEndDate: '2026-10-27' },
    });

    expect(deadline).toBe('2026-10-27');
  });
});
