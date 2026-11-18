import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { WorkTypeDetail } from '../../core/work-type.models';
import { WorkTypeDetailDrawerComponent } from './work-type-detail-drawer.component';

const workType: WorkTypeDetail = {
  id: 'w1',
  code: 'WT-T-001',
  name: 'Mở tài khoản',
  description: 'Mô tả công việc',
  productCode: null,
  status: 'ACTIVE',
  createdAt: '2026-10-12T09:30:00+07:00',
  kpis: [
    {
      kpiDefinitionId: 'k1',
      kpiCode: 'KPI-T01',
      kpiName: 'Tài khoản mới',
      isRequired: true,
      multiplier: 1.5,
      effectiveFrom: '2026-01-01',
      effectiveTo: null,
    },
  ],
};

describe('WorkTypeDetailDrawerComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<WorkTypeDetailDrawerComponent>;
  const worksUrl = `${environment.apiUrl}/works`;
  const root = () => fixture.nativeElement as HTMLElement;

  function setup(detail: WorkTypeDetail = workType) {
    TestBed.configureTestingModule({
      imports: [WorkTypeDetailDrawerComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(WorkTypeDetailDrawerComponent);
    fixture.componentRef.setInput('workType', detail);
    fixture.detectChanges();
  }

  const flushUsage = (open: number, completed: number) => {
    const requests = http.match((r) => r.url === worksUrl);
    expect(requests.length).toBe(2);
    requests.forEach((r) =>
      r.flush({
        success: true,
        data: {
          items: [],
          totalCount: r.request.params.get('status')?.includes('Done') ? completed : open,
          page: 1,
          pageSize: 1,
        },
      }),
    );
    fixture.detectChanges();
  };

  it('shows the work type fields, KPI mapping and the open / completed work counts', () => {
    setup();
    flushUsage(7, 3);
    const text = root().textContent ?? '';
    expect(text).toContain('WT-T-001');
    expect(text).toContain('Mở tài khoản');
    expect(text).toContain('KPI-T01');
    expect(text).toContain('×1.5');
    const usage = root().querySelector('.usage-grid')?.textContent ?? '';
    expect(usage).toContain('7');
    expect(usage).toContain('3');
  });

  it('shows the position, result unit and CIF requirement given by the list', () => {
    setup();
    fixture.componentRef.setInput('positionName', 'Teller');
    fixture.componentRef.setInput('resultUnit', 'tài khoản');
    fixture.componentRef.setInput('transactionCif', 'Bắt buộc');
    fixture.detectChanges();
    flushUsage(1, 1);
    const text = root().querySelector('.info-grid')?.textContent ?? '';
    expect(text).toContain('Teller');
    expect(text).toContain('tài khoản');
    expect(text).toContain('Bắt buộc');
  });

  it('shows N/A, not a made-up number, when the work counts cannot be read', () => {
    setup();
    const [first] = http.match((r) => r.url === worksUrl);
    first.flush('err', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    const cards = root().querySelectorAll('.usage-card strong');
    expect(cards.length).toBe(2);
    cards.forEach((card) => expect(card.textContent?.trim()).toBe('N/A'));
  });

  it('keeps the edit action disabled because editing is not available yet', () => {
    setup();
    flushUsage(0, 0);
    const buttons = root().querySelectorAll<HTMLButtonElement>('.drawer-footer .button-primary');
    expect(buttons.length).toBe(1);
    expect(buttons[0].disabled).toBeTrue();
  });

  it('deactivates after confirmation and tells the list to reload', () => {
    setup();
    flushUsage(0, 0);
    let changed = 0;
    fixture.componentInstance.changed.subscribe(() => changed++);

    root().querySelector<HTMLElement>('.danger-button')?.click();
    fixture.detectChanges();
    expect(root().querySelector('.confirm-text')).not.toBeNull();
    root().querySelector<HTMLElement>('.drawer-footer .button-primary')?.click();

    const patch = http.expectOne(`${environment.apiUrl}/work-types/w1/status`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ status: 'INACTIVE' });
    patch.flush({ success: true, status: 200, data: null });
    expect(changed).toBe(1);
  });

  it('activates an inactive work type', () => {
    setup({ ...workType, status: 'INACTIVE' });
    flushUsage(0, 0);
    root().querySelector<HTMLElement>('.danger-button')?.click();
    fixture.detectChanges();
    root().querySelector<HTMLElement>('.drawer-footer .button-primary')?.click();

    const patch = http.expectOne(`${environment.apiUrl}/work-types/w1/status`);
    expect(patch.request.body).toEqual({ status: 'ACTIVE' });
    patch.flush({ success: true, status: 200, data: null });
  });

  it('shows the real error and does not reload when changing the status fails', () => {
    setup();
    flushUsage(0, 0);
    let changed = 0;
    fixture.componentInstance.changed.subscribe(() => changed++);

    root().querySelector<HTMLElement>('.danger-button')?.click();
    fixture.detectChanges();
    root().querySelector<HTMLElement>('.drawer-footer .button-primary')?.click();
    http
      .expectOne(`${environment.apiUrl}/work-types/w1/status`)
      .flush({ success: false }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(root().querySelector('.action-error')).not.toBeNull();
    expect(changed).toBe(0);
  });

  it('emits closeDrawer when the close button is pressed', () => {
    setup();
    flushUsage(0, 0);
    let closed = 0;
    fixture.componentInstance.closeDrawer.subscribe(() => closed++);
    root().querySelector<HTMLElement>('.close-button')?.click();
    expect(closed).toBe(1);
  });

  it('renders the Vietnamese labels, then the English ones after the language is switched', () => {
    setup();
    flushUsage(0, 0);
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');
    fixture.detectChanges();
    const vi = i18n.localization.workTypes;
    expect(root().querySelector('.usage-grid')?.textContent).toContain(vi.usageOpen);
    expect(root().querySelector('.drawer')?.getAttribute('aria-label')).toBe(vi.drawerTitle);

    i18n.setLanguage('en');
    fixture.detectChanges();
    const en = i18n.localization.workTypes;
    expect(en.usageOpen).not.toBe(vi.usageOpen);
    expect(root().querySelector('.usage-grid')?.textContent).toContain(en.usageOpen);
    expect(root().querySelector('.drawer')?.getAttribute('aria-label')).toBe(en.drawerTitle);
  });
});
