import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { JobPositionDetail } from '../../core/job-position.models';
import { JobPositionService } from '../../core/job-position.service';
import { JobPositionDetailDrawerComponent } from './job-position-detail-drawer.component';

function detail(code: string, extra: Partial<JobPositionDetail> = {}): JobPositionDetail {
  return {
    id: `id-${code}`,
    code,
    title: `Title ${code}`,
    description: `About ${code}`,
    employeeCount: 14,
    lastSyncAt: '2026-10-24T08:15:00+07:00',
    status: 'ACTIVE',
    evaluationProfile: {
      id: 'ep-1',
      code: 'EP-1',
      name: 'Profile name',
      description: null,
      version: 2,
      effectiveFrom: '2026-01-01',
      status: 'ACTIVE',
      totalWeightPercent: 70.5,
      items: [
        {
          id: 'i1',
          groupName: 'Kinh doanh',
          groupCode: 'KD',
          weightPercent: 20.5,
          displayOrder: 1,
        },
        {
          id: 'i2',
          groupName: 'Chất lượng',
          groupCode: 'CLDV',
          weightPercent: 50,
          displayOrder: 2,
        },
      ],
    },
    workTypes: [
      {
        id: 'wt-1',
        code: 'MO_TK',
        name: 'Mở tài khoản',
        description: null,
        kpiCount: 2,
        kpis: [
          { id: 'k1', code: 'KH_MOI', name: 'Khách hàng mới', isRequired: true, unit: 'Tài khoản' },
          { id: 'k2', code: 'CSAT', name: 'Điểm hài lòng', isRequired: false, unit: 'Điểm' },
        ],
      },
    ],
    ...extra,
  };
}

describe('JobPositionDetailDrawerComponent', () => {
  let fixture: ComponentFixture<JobPositionDetailDrawerComponent>;
  let service: jasmine.SpyObj<JobPositionService>;
  let i18n: I18nService;

  const root = () => fixture.nativeElement as HTMLElement;
  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  function open(id = 'id-TELLER') {
    fixture = TestBed.createComponent(JobPositionDetailDrawerComponent);
    fixture.componentRef.setInput('positionId', id);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    service = jasmine.createSpyObj<JobPositionService>('JobPositionService', ['detail']);
    service.detail.and.callFake((id: string) => of(detail(id.replace('id-', ''))));
    TestBed.configureTestingModule({
      imports: [JobPositionDetailDrawerComponent],
      providers: [{ provide: JobPositionService, useValue: service }],
    });
    i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');
  });

  it('calls the detail endpoint for the selected position and renders the response', () => {
    open();

    expect(service.detail).toHaveBeenCalledOnceWith('id-TELLER');
    expect(text(root().querySelector('.code-badge'))).toBe('TELLER');
    expect(text(root().querySelector('.drawer-title'))).toBe('Title TELLER');
    expect(text(root().querySelector('.status-badge'))).toBe('HOẠT ĐỘNG');
    expect(text(root().querySelector('.position-desc'))).toBe('About TELLER');
    expect(text(root().querySelector('.stat-value'))).toBe('14');
  });

  it('renders the evaluation profile with decimal weights and the exact total from the API', () => {
    open();

    expect(text(root().querySelector('.criteria-name'))).toBe('Profile name');
    expect(text(root().querySelector('.criteria-date'))).toBe('Ngày hiệu lực: 01/01/2026');
    const weights = Array.from(root().querySelectorAll('.criteria-card tbody .col-num')).map((c) =>
      text(c),
    );
    expect(weights).toEqual(['20.5%', '50%']);
    expect(text(root().querySelector('.total-val'))).toBe('70.5%');
  });

  it('renders work types and KPI items with the required/optional flag', () => {
    open();

    expect(
      Array.from(root().querySelectorAll('.work-types-table-wrap tbody tr td'))
        .map((c) => text(c))
        .join('|'),
    ).toBe('MO_TK|Mở tài khoản|2');
    const rows = Array.from(root().querySelectorAll('.kpi-item-row'));
    expect(rows.map((r) => text(r.querySelector('.kpi-meta')))).toEqual([
      'KH_MOI · Tài khoản',
      'CSAT · Điểm',
    ]);
    expect(rows.map((r) => text(r.querySelector('.kpi-req-badge')))).toEqual([
      'Bắt buộc',
      'Tùy chọn',
    ]);
    expect(rows[0].querySelector('.kpi-req-badge')?.classList).toContain('is-req');
  });

  it('uses the number of listed work types, not a stored figure', () => {
    service.detail.and.returnValue(
      of(
        detail('TELLER', {
          workTypes: [detail('x').workTypes[0], { ...detail('y').workTypes[0], id: 'wt-2' }],
        }),
      ),
    );
    open();

    expect(text(root().querySelectorAll('.stat-value')[1])).toBe('2');
  });

  it('shows the empty message, not invented rows, when there is no profile or work type', () => {
    service.detail.and.returnValue(
      of(detail('BARE', { description: null, evaluationProfile: null, workTypes: [] })),
    );
    open();

    expect(root().querySelector('.criteria-card')).toBeNull();
    expect(root().querySelectorAll('.muted-text').length).toBe(3);
    expect(text(root().querySelector('.muted-text'))).toBe('Không có dữ liệu');
    expect(root().querySelector('.kpi-item-row')).toBeNull();
    expect(text(root().querySelector('.position-desc'))).toBe('N/A');
  });

  it('marks an inactive position as inactive', () => {
    service.detail.and.returnValue(of(detail('OLD', { status: 'INACTIVE' })));
    open();

    expect(text(root().querySelector('.status-badge'))).toBe('NGỪNG HOẠT ĐỘNG');
    expect(root().querySelector('.status-badge')?.classList).toContain('is-muted');
  });

  it('shows the loading state first', () => {
    service.detail.and.returnValue(new Subject<JobPositionDetail>());
    open();

    expect(text(root().querySelector('.state-card'))).toBe('Đang tải dữ liệu...');
    expect(root().querySelector('section')?.getAttribute('aria-busy')).toBe('true');
  });

  it('shows an error with a working retry and clears stale data', () => {
    service.detail.and.returnValues(
      throwError(() => new Error('500')),
      of(detail('TELLER')),
    );
    open();

    expect(root().querySelector('[role="alert"]')).not.toBeNull();
    expect(root().querySelector('.criteria-card')).toBeNull();
    expect(text(root().querySelector('[role="alert"] button'))).toBe('Thử lại');

    root().querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    fixture.detectChanges();

    expect(service.detail).toHaveBeenCalledTimes(2);
    expect(root().querySelector('[role="alert"]')).toBeNull();
    expect(text(root().querySelector('.drawer-title'))).toBe('Title TELLER');
  });

  it('reloads when another position is selected and ignores the stale response', () => {
    const first = new Subject<JobPositionDetail>();
    service.detail.and.callFake((id: string) => (id === 'id-A' ? first : of(detail('B'))));
    open('id-A');

    fixture.componentRef.setInput('positionId', 'id-B');
    fixture.detectChanges();
    first.next(detail('A', { title: 'STALE' }));
    fixture.detectChanges();

    expect(service.detail).toHaveBeenCalledWith('id-A');
    expect(service.detail).toHaveBeenCalledWith('id-B');
    expect(text(root().querySelector('.drawer-title'))).toBe('Title B');
    expect(root().textContent).not.toContain('STALE');
  });

  it('clears the previous position while the next one loads', () => {
    const next = new Subject<JobPositionDetail>();
    service.detail.and.callFake((id: string) => (id === 'id-B' ? next : of(detail('A'))));
    open('id-A');

    fixture.componentRef.setInput('positionId', 'id-B');
    fixture.detectChanges();

    expect(root().querySelector('.drawer-title')?.textContent?.trim() ?? '').toBe('');
    expect(root().querySelector('.state-card')).not.toBeNull();
  });

  it('emits closeDrawer from the close button, which has a localized accessible name', () => {
    open();
    let closed = 0;
    fixture.componentInstance.closeDrawer.subscribe(() => closed++);
    const button = root().querySelector<HTMLButtonElement>('.close-btn');

    expect(button?.getAttribute('aria-label')).toBe('Đóng');
    button?.click();
    expect(closed).toBe(1);
  });

  it('formats the effective date and numbers with the shared helpers', () => {
    open();
    const drawer = fixture.componentInstance;

    expect(drawer.formatEffectiveDate('2026-03-05')).toBe('05/03/2026');
    expect(drawer.formatEffectiveDate(undefined)).toBe('-');
    expect(drawer.number(null)).toBe('N/A');
    expect(drawer.number(1234)).toBe('1.234');
  });

  it('switches every label and accessibility text language live', () => {
    open();
    expect(root().querySelector('section')?.getAttribute('aria-label')).toBe(
      'Chi tiết vị trí công việc',
    );

    i18n.setLanguage('en');
    fixture.detectChanges();

    expect(root().querySelector('section')?.getAttribute('aria-label')).toBe(
      'Job Position Details',
    );
    expect(root().querySelector('.close-btn')?.getAttribute('aria-label')).toBe('Close');
    expect(text(root().querySelector('.status-badge'))).toBe('ACTIVE');
    expect(text(root().querySelector('.criteria-date'))).toBe('Effective Date: 01/01/2026');
    expect(text(root().querySelectorAll('.kpi-req-badge')[0])).toBe('Required');
    expect(text(root().querySelector('.section-heading'))).toBe('General Information');
  });
});
