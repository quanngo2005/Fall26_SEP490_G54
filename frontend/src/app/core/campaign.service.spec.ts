import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { BatchWorkPayload, CreateCampaignPayload } from './campaign.models';
import { CampaignService } from './campaign.service';

describe('CampaignService', () => {
  let service: CampaignService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/campaigns`;

  const envelope = <T>(data: T) => ({ success: true, status: 200, data });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CampaignService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists campaigns and sends only the filters that are set', () => {
    let page: number | undefined;
    service
      .list({ search: 'CMP', status: '', objectiveId: undefined, page: 2, pageSize: 6 })
      .subscribe((overview) => (page = overview.campaigns.page));

    const request = http.expectOne((r) => r.url === url);
    expect(request.request.params.get('search')).toBe('CMP');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.has('status')).toBe(false);
    expect(request.request.params.has('objectiveId')).toBe(false);
    request.flush(
      envelope({ metrics: {}, campaigns: { items: [], totalCount: 0, page: 2, pageSize: 6 } }),
    );
    expect(page).toBe(2);
  });

  it('reads one campaign by id', () => {
    let code = '';
    service.detail('c 1').subscribe((campaign) => (code = campaign.code));

    const request = http.expectOne(`${url}/c%201`);
    expect(request.request.method).toBe('GET');
    request.flush(envelope({ code: 'CMP-1' }));
    expect(code).toBe('CMP-1');
  });

  it('loads the business objectives for the plan form', () => {
    let count = 0;
    service.objectives().subscribe((objectives) => (count = objectives.length));

    http
      .expectOne(`${environment.apiUrl}/business-objectives`)
      .flush(envelope([{ id: 'o1', code: 'OBJ-1', name: 'Objective', description: null }]));
    expect(count).toBe(1);
  });

  it('creates a campaign plan', () => {
    const payload: CreateCampaignPayload = {
      businessObjectiveId: 'o1',
      code: 'CMP-1',
      name: 'Plan',
      description: null,
      startDate: '2026-11-01',
      endDate: '2026-12-31',
      workTypeIds: ['w1'],
      participantEmployeeIds: ['e1'],
    };
    service.create(payload).subscribe();

    const request = http.expectOne(url);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(payload);
    request.flush(envelope({ id: 'c1' }));
  });

  it('generates batch work for a campaign', () => {
    const payload: BatchWorkPayload = {
      workTypeId: 'w1',
      leadPoolSource: 'Pool',
      allocationRule: 'EQUAL_SPLIT',
      totalLeadsCount: 36,
      plannedEndDate: null,
      targetEmployeeIds: ['e1'],
      targetValue: 600,
    };
    let generated = 0;
    service
      .batchGenerateWork('c1', payload)
      .subscribe((result) => (generated = result.totalGenerated));

    const request = http.expectOne(`${url}/c1/batch-generate-work`);
    expect(request.request.body).toEqual(payload);
    request.flush(envelope({ totalGenerated: 36 }));
    expect(generated).toBe(36);
  });

  it('rejects an unsuccessful envelope', () => {
    let failed = false;
    service.detail('c1').subscribe({ error: () => (failed = true) });

    http.expectOne(`${url}/c1`).flush({ success: false, status: 200, data: null });
    expect(failed).toBe(true);
  });
});
