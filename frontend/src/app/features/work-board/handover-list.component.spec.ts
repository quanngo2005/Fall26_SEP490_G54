import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { HandoverListItem } from '../../core/handover.models';
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
});
