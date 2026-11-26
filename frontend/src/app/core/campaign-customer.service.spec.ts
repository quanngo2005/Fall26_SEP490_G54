import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { CampaignCustomerService } from './campaign-customer.service';

describe('CampaignCustomerService', () => {
  let service: CampaignCustomerService;
  let http: HttpTestingController;
  const campaigns = `${environment.apiUrl}/campaigns/c1/customers`;
  const mine = `${environment.apiUrl}/my-campaigns`;
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CampaignCustomerService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists customers and sends only the filters that are set', () => {
    service
      .list('c1', { search: 'An', status: '', unassignedOnly: false, assigneeId: 'e1', page: 2 })
      .subscribe();

    const request = http.expectOne((r) => r.url === campaigns);
    expect(request.request.params.get('search')).toBe('An');
    expect(request.request.params.get('assigneeId')).toBe('e1');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.has('status')).toBe(false);
    expect(request.request.params.has('unassignedOnly')).toBe(false);
    request.flush(
      envelope({
        page: { items: [], totalCount: 0, page: 2, pageSize: 20 },
        summary: { total: 0, unassigned: 0, byStatus: [], byAssignee: [] },
      }),
    );
  });

  it('keeps the unassigned filter when it is on', () => {
    service.list('c1', { unassignedOnly: true }).subscribe();

    const request = http.expectOne((r) => r.url === campaigns);
    expect(request.request.params.get('unassignedOnly')).toBe('true');
    request.flush(envelope({ page: {}, summary: {} }));
  });

  it('allocates with the rule and the chosen employees', () => {
    service.allocate('c1', { rule: 'EQUAL_SPLIT', employeeIds: ['e1'] }).subscribe();

    const request = http.expectOne(`${campaigns}/allocate`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ rule: 'EQUAL_SPLIT', employeeIds: ['e1'] });
    request.flush(envelope({ assignedCount: 1, byEmployee: [] }));
  });

  it('reassigns one customer with PUT', () => {
    service.reassign('c1', 'k1', 'e2').subscribe();

    const request = http.expectOne(`${campaigns}/k1/assignee`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ employeeId: 'e2' });
    request.flush(envelope({ id: 'k1' }));
  });

  it('uploads the file as multipart for preview and commit', () => {
    const file = new File(['x'], 'khach-hang.xlsx');
    service.importPreview('c1', file).subscribe();
    service.importCommit('c1', file).subscribe();

    const preview = http.expectOne(`${campaigns}/import/preview`);
    const commit = http.expectOne(`${campaigns}/import/commit`);
    expect((preview.request.body as FormData).get('file')).toBeInstanceOf(File);
    expect((commit.request.body as FormData).get('file')).toBeInstanceOf(File);
    preview.flush(envelope({}));
    commit.flush(envelope({}));
  });

  it('reads my campaigns and my customers', () => {
    service.myCampaigns().subscribe();
    service.myCustomers('c1', { status: 'CALLED' }).subscribe();

    http.expectOne(mine).flush(envelope([]));
    const customers = http.expectOne((r) => r.url === `${mine}/c1/customers`);
    expect(customers.request.params.get('status')).toBe('CALLED');
    customers.flush(envelope({ items: [], totalCount: 0, page: 1, pageSize: 20 }));
  });

  it('patches the contact status with the transaction codes', () => {
    const payload = {
      status: 'CLOSED' as const,
      callAnswered: null,
      appointmentAt: null,
      metAt: null,
      transactionCodes: ['TX-1'],
      note: null,
    };
    service.updateContact('c1', 'k1', payload).subscribe();

    const request = http.expectOne(`${mine}/c1/customers/k1/contact`);
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual(payload);
    request.flush(envelope({ id: 'k1' }));
  });
});
