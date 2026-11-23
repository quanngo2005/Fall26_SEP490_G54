import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { DepartmentMember } from '../../core/department.models';
import { HandoverListItem } from '../../core/handover.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { HandoverListComponent } from './handover-list.component';

const item = (overrides: Partial<HandoverListItem> = {}): HandoverListItem => ({
  id: 'h-1',
  handoverCode: 'HO-000001',
  workId: 'w-1',
  workCode: 'WK-1038',
  workTitle: 'E-banking - Đăng ký',
  workStatus: 'HANDED_OVER',
  workTypeName: 'E-banking',
  customerName: 'Trần Thị B',
  customerCif: 'CIF 0012345',
  targetValue: null,
  plannedEndDate: '2026-10-27',
  fromEmployeeId: 'e-1',
  fromEmployeeCode: 'NV01',
  fromEmployeeName: 'Nguyễn Văn A',
  fromJobPositionTitle: 'Giao dịch viên',
  toEmployeeId: 'e-2',
  toEmployeeCode: 'NV02',
  toEmployeeName: 'Phạm Văn D',
  toJobPositionTitle: 'CV bán hàng',
  reason: 'Vượt ngưỡng giao dịch tại quầy',
  referrerSharePercent: 30,
  status: 'ACCEPTED',
  kpiStatus: 'PENDING',
  handoverAt: '2026-10-24T08:00:00Z',
  acceptedAt: '2026-10-24T08:00:00Z',
  updatedAt: '2026-10-24T08:00:00Z',
  isReadOnly: true,
  ...overrides,
});

const member = (id: string, name: string, status: DepartmentMember['status'] = 'ACTIVE') =>
  ({
    id,
    employeeCode: `NV-${id}`,
    fullName: name,
    status,
  }) as DepartmentMember;

describe('HandoverListComponent', () => {
  let fixture: ComponentFixture<HandoverListComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HandoverListComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(HandoverListComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('loads incoming handovers and opens the read-only detail drawer (SCR-WRK-05-D1)', () => {
    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/handovers`);
    expect(req.request.params.get('direction')).toBe('INCOMING');
    req.flush({
      success: true,
      data: { items: [item()], totalCount: 1, page: 1, pageSize: 20 },
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('HO-000001');
    expect(text).toContain('WK-1038');
    expect(text).toContain('Trần Thị B');

    const row = fixture.nativeElement.querySelector('tbody tr') as HTMLTableRowElement;
    row.click();
    fixture.detectChanges();

    const detailReq = http.expectOne(`${environment.apiUrl}/handovers/h-1`);
    detailReq.flush({ success: true, data: item({ kpiStatus: 'RECORDED' }) });
    fixture.detectChanges();

    const drawer = fixture.nativeElement.querySelector('.handover-drawer') as HTMLElement;
    expect(drawer).toBeTruthy();
    expect(drawer.textContent).toContain('Vượt ngưỡng giao dịch tại quầy');
    expect(drawer.textContent).toContain('Đã ghi');
  });

  it('switches direction to OUTGOING and emits openWork when clicking open work', () => {
    http
      .expectOne((r) => r.url === `${environment.apiUrl}/handovers`)
      .flush({ success: true, data: { items: [item()], totalCount: 1, page: 1, pageSize: 20 } });
    fixture.detectChanges();

    const opened: string[] = [];
    fixture.componentInstance.openWork.subscribe((id) => opened.push(id));

    (fixture.nativeElement.querySelector('#handover-dir-outgoing') as HTMLButtonElement).click();
    fixture.detectChanges();

    const outgoingReq = http.expectOne((r) => r.url === `${environment.apiUrl}/handovers`);
    expect(outgoingReq.request.params.get('direction')).toBe('OUTGOING');
    outgoingReq.flush({
      success: true,
      data: { items: [item()], totalCount: 1, page: 1, pageSize: 20 },
    });
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.link-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/handovers/h-1`).flush({ success: true, data: item() });
    fixture.detectChanges();

    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll(
        '.drawer-foot button',
      ) as NodeListOf<HTMLButtonElement>,
    );
    buttons[buttons.length - 1].click();
    expect(opened).toEqual(['w-1']);
  });

  describe('manager decisions on a pending request', () => {
    const url = () => `${environment.apiUrl}/handovers`;
    const awaiting = (overrides: Partial<HandoverListItem> = {}) =>
      item({ status: 'PENDING', acceptedAt: null, isReadOnly: false, ...overrides });

    /** Loads one pending request and opens its drawer, as the requester's manager. */
    function openPending(overrides: Partial<HandoverListItem> = {}): void {
      fixture.componentRef.setInput('members', [
        member('e-1', 'Nguyen A'),
        member('e-2', 'Pham D'),
        member('e-3', 'Le C'),
        member('e-4', 'Tran E', 'INACTIVE'),
      ]);
      http
        .expectOne((r) => r.url === url())
        .flush({
          success: true,
          data: { items: [awaiting(overrides)], totalCount: 1, page: 1, pageSize: 20 },
        });
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('tbody tr') as HTMLTableRowElement).click();
      fixture.detectChanges();
      http.expectOne(`${url()}/h-1`).flush({ success: true, data: awaiting(overrides) });
      fixture.detectChanges();
    }

    const footerButtons = () =>
      Array.from(
        fixture.nativeElement.querySelectorAll(
          '.drawer-foot button',
        ) as NodeListOf<HTMLButtonElement>,
      );
    const buttonLabels = () => footerButtons().map((b) => b.textContent?.trim());
    const reload = () => {
      http
        .expectOne((r) => r.url === url())
        .flush({ success: true, data: { items: [], totalCount: 0, page: 1, pageSize: 20 } });
      fixture.detectChanges();
    };

    it("offers approve, change recipient and reject only to the requester's manager", () => {
      openPending();

      expect(buttonLabels()).toEqual(['Từ chối', 'Đổi người nhận', 'Duyệt', 'Mở việc liên quan']);
    });

    it('never offers accept or any decision to the recipient or on a decided request', () => {
      openPending({ isReadOnly: true });

      expect(buttonLabels()).toEqual(['Mở việc liên quan']);
      expect(fixture.nativeElement.textContent).not.toContain('Chấp nhận');
    });

    it('does not offer a decision on a request that is already decided', () => {
      openPending({ status: 'ACCEPTED' });

      expect(buttonLabels()).toEqual(['Mở việc liên quan']);
    });

    it('approves, tells the board and refreshes the list', () => {
      const notices: { kind: string; text: string }[] = [];
      fixture.componentInstance.changed.subscribe((n) => notices.push(n));
      openPending();

      footerButtons()[2].click();
      const req = http.expectOne(`${url()}/h-1/manager/approve`);
      expect(req.request.method).toBe('POST');
      req.flush({ success: true, data: { id: 'h-1', status: 'ACCEPTED' } });
      fixture.detectChanges();

      expect(notices).toEqual([{ kind: 'success', text: 'Đã duyệt bàn giao HO-000001.' }]);
      expect(fixture.nativeElement.querySelector('.handover-drawer')).toBeNull();
      reload();
    });

    it('rejects and refreshes the list', () => {
      const notices: { kind: string; text: string }[] = [];
      fixture.componentInstance.changed.subscribe((n) => notices.push(n));
      openPending();

      footerButtons()[0].click();
      http
        .expectOne(`${url()}/h-1/manager/reject`)
        .flush({ success: true, data: { id: 'h-1', status: 'REJECTED' } });
      fixture.detectChanges();

      expect(notices).toEqual([{ kind: 'success', text: 'Đã từ chối bàn giao HO-000001.' }]);
      reload();
    });

    it('reassigns to an active colleague who is neither the giver nor the current recipient', () => {
      const notices: { kind: string; text: string }[] = [];
      fixture.componentInstance.changed.subscribe((n) => notices.push(n));
      openPending();

      footerButtons()[1].click();
      fixture.detectChanges();
      const confirm = () => footerButtons().find((b) => b.textContent?.trim() === 'Xác nhận')!;
      expect(confirm().disabled).toBe(true);

      const options = (
        fixture.componentInstance as unknown as {
          reassignOptions: () => { value: string }[];
        }
      ).reassignOptions();
      expect(options.map((o) => o.value)).toEqual(['', 'e-3']);

      (
        fixture.componentInstance as unknown as { newRecipient: { set: (v: string) => void } }
      ).newRecipient.set('e-3');
      fixture.detectChanges();
      expect(confirm().disabled).toBe(false);
      confirm().click();

      const req = http.expectOne(`${url()}/h-1/manager/reassign`);
      expect(req.request.body).toEqual({ toEmployeeId: 'e-3' });
      req.flush({ success: true, data: { id: 'h-1', status: 'ACCEPTED' } });
      fixture.detectChanges();

      expect(notices[0]).toEqual({
        kind: 'success',
        text: 'Đã đổi người nhận và chuyển việc cho bàn giao HO-000001.',
      });
      reload();
    });

    it('goes back from the reassign picker without calling the API', () => {
      openPending();

      footerButtons()[1].click();
      fixture.detectChanges();
      footerButtons()
        .find((b) => b.textContent?.trim() === 'Hủy')!
        .click();
      fixture.detectChanges();

      expect(buttonLabels()).toEqual(['Từ chối', 'Đổi người nhận', 'Duyệt', 'Mở việc liên quan']);
      http.expectNone(`${url()}/h-1/manager/reassign`);
    });

    it('explains a request that was already handled and refreshes the list', () => {
      const notices: { kind: string; text: string }[] = [];
      fixture.componentInstance.changed.subscribe((n) => notices.push(n));
      openPending();

      footerButtons()[2].click();
      http
        .expectOne(`${url()}/h-1/manager/approve`)
        .flush(
          { success: false, status: 409, errorCode: 'HANDOVER_NOT_PENDING' },
          { status: 409, statusText: 'Conflict' },
        );
      fixture.detectChanges();

      expect(notices).toEqual([
        { kind: 'error', text: 'Yêu cầu này đã được xử lý. Danh sách đã được làm mới.' },
      ]);
      reload();
    });

    it('tells the manager when the work can no longer be handed over', () => {
      const notices: { kind: string; text: string }[] = [];
      fixture.componentInstance.changed.subscribe((n) => notices.push(n));
      openPending();

      footerButtons()[2].click();
      http
        .expectOne(`${url()}/h-1/manager/approve`)
        .flush(
          { success: false, status: 409, errorCode: 'WORK_INVALID_STATE' },
          { status: 409, statusText: 'Conflict' },
        );
      fixture.detectChanges();

      expect(notices[0].text).toBe('Công việc không còn ở trạng thái cho phép bàn giao.');
      reload();
    });

    it('reports a forbidden decision and keeps the drawer open', () => {
      const notices: { kind: string; text: string }[] = [];
      fixture.componentInstance.changed.subscribe((n) => notices.push(n));
      openPending();

      footerButtons()[0].click();
      http
        .expectOne(`${url()}/h-1/manager/reject`)
        .flush(
          { success: false, status: 403, errorCode: 'FORBIDDEN' },
          { status: 403, statusText: 'Forbidden' },
        );
      fixture.detectChanges();

      expect(notices[0]).toEqual({
        kind: 'error',
        text: 'Chỉ quản lý trực tiếp của người bàn giao mới được xử lý yêu cầu này.',
      });
      expect(fixture.nativeElement.querySelector('.handover-drawer')).not.toBeNull();
      http.expectNone((r) => r.url === url());
    });

    it('shows a generic error and keeps the drawer open on any other failure', () => {
      const notices: { kind: string; text: string }[] = [];
      fixture.componentInstance.changed.subscribe((n) => notices.push(n));
      openPending();

      footerButtons()[2].click();
      http
        .expectOne(`${url()}/h-1/manager/approve`)
        .flush({}, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(notices[0].kind).toBe('error');
      expect(notices[0].text).toBe('Không thể xử lý yêu cầu bàn giao. Vui lòng thử lại.');
      expect(footerButtons().every((b) => !b.disabled)).toBe(true);
    });

    it('leaves Escape and the arrow keys to the recipient picker while reassigning', () => {
      openPending();
      footerButtons()[1].click();
      fixture.detectChanges();
      const picker = fixture.nativeElement.querySelector('.reassign-row') as HTMLElement;

      for (const key of ['Escape', 'ArrowDown', 'ArrowUp']) {
        picker.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
      }
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.handover-drawer')).not.toBeNull();
      http.expectNone((r) => r.url === `${environment.apiUrl}/handovers/h-1`);
    });

    it('still closes the drawer with Escape outside the recipient picker', () => {
      openPending();

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.handover-drawer')).toBeNull();
    });

    it('switches the decision buttons between Vietnamese and English live', () => {
      openPending();
      expect(buttonLabels()[2]).toBe('Duyệt');

      TestBed.inject(I18nService).setLanguage('en');
      fixture.detectChanges();
      expect(buttonLabels()).toEqual([
        'Reject',
        'Change recipient',
        'Approve',
        'Open related work',
      ]);

      TestBed.inject(I18nService).setLanguage('vi');
    });
  });
});
