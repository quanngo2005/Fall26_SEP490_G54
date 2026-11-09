import { HttpErrorResponse } from '@angular/common/http';
import { Component, input, output, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { JobPositionService } from '../../core/job-position.service';
import {
  KpiDefinition,
  KpiGroupOption,
  KpiPeriodRef,
  KpiSummary,
} from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { KpiDecision, KpiDecisionModalComponent } from './kpi-decision-modal.component';
import { KpiDetailDrawerComponent, KpiViewerMode } from './kpi-detail-drawer.component';
import { KpiDefinitionsComponent } from './kpi-definitions.component';
import {
  KpiFormMode,
  KpiFormModalComponent,
  KpiFormResult,
  KpiFormValues,
} from './kpi-form-modal.component';

@Component({ selector: 'app-header', standalone: true, template: '' })
class HeaderStub {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class SidebarStub {}

@Component({ selector: 'app-kpi-detail-drawer', standalone: true, template: '' })
class DrawerStub {
  readonly kpi = input.required<KpiDefinition>();
  readonly mode = input<KpiViewerMode>('viewer');
  readonly closed = output<void>();
  readonly decide = output<KpiDecision>();
  readonly edit = output<void>();
  readonly submitForApproval = output<void>();
}

@Component({ selector: 'app-kpi-form-modal', standalone: true, template: '' })
class FormStub {
  readonly mode = input.required<KpiFormMode>();
  readonly kpi = input<KpiDefinition | null>(null);
  readonly groups = input<readonly KpiGroupOption[]>([]);
  readonly positions = input<readonly unknown[]>([]);
  readonly periods = input<readonly EvaluationPeriodOption[]>([]);
  readonly products = input<readonly string[]>([]);
  readonly units = input<readonly string[]>([]);
  readonly prefill = input<Partial<KpiFormValues> | null>(null);
  readonly canSubmit = input(true);
  readonly closed = output<void>();
  readonly saved = output<KpiFormResult>();
}

@Component({ selector: 'app-kpi-decision-modal', standalone: true, template: '' })
class DecisionStub {
  readonly kpi = input.required<KpiDefinition>();
  readonly initial = input<KpiDecision>('APPROVE');
  readonly closed = output<void>();
  readonly decided = output<{ decision: KpiDecision; kpi: KpiDefinition }>();
}

const period = (code: string, endDate: string): KpiPeriodRef => ({
  id: code,
  code,
  name: code,
  startDate: '2026-01-01',
  endDate,
});

const item = (code: string, extra: Partial<KpiDefinition> = {}): KpiDefinition =>
  ({
    id: code,
    code,
    name: `KPI ${code}`,
    kpiGroupId: 'g1',
    kpiGroupName: 'Sales',
    unit: 'VND',
    aggregationType: 'SUM',
    direction: 'HIGHER_BETTER',
    maxAchievementPercent: null,
    status: 'ACTIVE',
    productName: null,
    creditRole: 'PROCESSOR',
    dataSource: 'TRANSACTION',
    jobPositions: [{ id: 'p1', code: 'TELLER', title: 'Teller' }],
    effectiveFrom: period('2026-Q1', '2026-03-31'),
    effectiveTo: null,
    submittedAt: null,
    rejectReason: null,
    requestNote: null,
    ...extra,
  }) as KpiDefinition;

const SUMMARY: KpiSummary = {
  total: 24,
  draft: 1,
  pendingApproval: 2,
  rejected: 0,
  active: 18,
  inactive: 3,
  archived: 0,
};

const QUARTERS = [
  {
    id: 'q4',
    code: '2026-Q4',
    name: 'Quý 4/2026',
    periodType: 'QUARTER',
    startDate: '2026-10-01',
    endDate: '2026-12-31',
    status: 'OPEN',
    isActive: true,
  },
  {
    id: 'm10',
    code: '2026-10',
    name: 'Tháng 10',
    periodType: 'MONTH',
    startDate: '2026-10-01',
    endDate: '2026-10-31',
    status: 'OPEN',
    isActive: true,
  },
] as EvaluationPeriodOption[];

describe('KpiDefinitionsComponent', () => {
  const FILTERS_KEY = 'g54.kpi-list.filters';
  let api: jasmine.SpyObj<KpiDefinitionService>;
  let rows: KpiDefinition[];
  const permissions = signal<string[]>([]);

  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const root = (fixture: ComponentFixture<unknown>) => fixture.nativeElement as HTMLElement;
  const lastList = () => api.list.calls.mostRecent().args[0]!;

  async function create(
    userPermissions: string[],
  ): Promise<ComponentFixture<KpiDefinitionsComponent>> {
    permissions.set(userPermissions);
    const fixture = TestBed.createComponent(KpiDefinitionsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const click = (fixture: ComponentFixture<unknown>, selector: string, index = 0) => {
    root(fixture).querySelectorAll<HTMLElement>(selector)[index]?.click();
    fixture.detectChanges();
  };

  const choose = (fixture: ComponentFixture<unknown>, selector: string, value: string) => {
    const field = root(fixture).querySelector<HTMLSelectElement | HTMLInputElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event(field instanceof HTMLSelectElement ? 'change' : 'input'));
    fixture.detectChanges();
  };

  /** A select whose options are bound with ngValue is chosen by position. */
  const pick = (fixture: ComponentFixture<unknown>, selector: string, index: number) => {
    const field = root(fixture).querySelector<HTMLSelectElement>(selector)!;
    field.selectedIndex = index;
    field.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };

  const ADMIN = ['KPI_MANAGE'];
  const MANAGER = ['KPI_APPROVE', 'KPI_REQUEST'];

  beforeEach(async () => {
    rows = [
      item('KPI-CARD-P', { productName: 'Thẻ / E-banking', name: 'Mở thẻ – xử lý' }),
      item('KPI-CARD-R', {
        productName: 'Thẻ / E-banking',
        creditRole: 'REFERRER',
        status: 'PENDING_APPROVAL',
        submittedAt: '2026-10-22T10:12:00+07:00',
      }),
      item('KPI-OPS', { maxAchievementPercent: 150 }),
      item('KPI-OLD', { status: 'INACTIVE' }),
    ];
    localStorage.removeItem(FILTERS_KEY);
    api = jasmine.createSpyObj<KpiDefinitionService>('KpiDefinitionService', [
      'list',
      'summary',
      'groups',
      'changeStatus',
    ]);
    api.list.and.callFake(() =>
      of({ items: rows, totalCount: rows.length, page: 1, pageSize: 20 }),
    );
    api.summary.and.returnValue(of(SUMMARY));
    api.groups.and.returnValue(of([{ id: 'g1', code: 'KD', name: 'Sales' }]));
    api.changeStatus.and.returnValue(of(undefined));
    await TestBed.configureTestingModule({
      imports: [KpiDefinitionsComponent],
      providers: [
        { provide: KpiDefinitionService, useValue: api },
        { provide: AuthService, useValue: { currentUser: () => ({ permissions: permissions() }) } },
        {
          provide: JobPositionService,
          useValue: { list: () => of({ items: [{ id: 'p1', code: 'TELLER', title: 'Teller' }] }) },
        },
        { provide: EvaluationService, useValue: { periods: () => of(QUARTERS) } },
      ],
    })
      .overrideComponent(KpiDefinitionsComponent, {
        remove: {
          imports: [
            HeaderComponent,
            SidebarComponent,
            KpiFormModalComponent,
            KpiDecisionModalComponent,
            KpiDetailDrawerComponent,
          ],
        },
        add: { imports: [HeaderStub, SidebarStub, FormStub, DecisionStub, DrawerStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => localStorage.removeItem(FILTERS_KEY));

  describe('as the System Admin', () => {
    it('groups the KPIs by product and shows the catalog columns', async () => {
      const fixture = await create(ADMIN);

      expect(Array.from(root(fixture).querySelectorAll('thead th')).map((th) => text(th))).toEqual([
        'MÃ',
        'TÊN KPI',
        'VỊ TRÍ',
        'CREDIT',
        'NGUỒN',
        'TỔNG HỢP',
        'TRẦN',
        'HIỆU LỰC',
        'TRẠNG THÁI',
        'THAO TÁC',
      ]);
      expect(Array.from(root(fixture).querySelectorAll('.group-row')).map((r) => text(r))).toEqual([
        'Sản phẩm: Thẻ / E-banking',
        'Không theo sản phẩm',
      ]);
      const first = root(fixture).querySelector('.kpi-row')!;
      expect(text(first)).toContain('KPI-CARD-P');
      expect(text(first)).toContain('Teller');
      expect(text(first)).toContain('Xử lý');
      expect(text(first)).toContain('Transaction');
      expect(text(first)).toContain('(VND)');
      expect(text(first)).toContain('Q1/2026 → không hạn');
      expect(text(root(fixture).querySelectorAll('.kpi-row')[2])).toContain('150%');
    });

    it('shows the tabs with their counts', async () => {
      const fixture = await create(ADMIN);

      expect(Array.from(root(fixture).querySelectorAll('.tab')).map((tab) => text(tab))).toEqual([
        'Tất cả · 24',
        'Chờ duyệt · 2',
        'Active · 18',
        'Draft · 1',
        'Inactive · 3',
      ]);
      expect(text(root(fixture).querySelector('.tab.is-active'))).toBe('Tất cả · 24');
    });

    it('shows a Rejected tab only when something was rejected', async () => {
      api.summary.and.returnValue(of({ ...SUMMARY, rejected: 2 }));
      const fixture = await create(ADMIN);

      expect(Array.from(root(fixture).querySelectorAll('.tab')).map((tab) => text(tab))).toContain(
        'Từ chối · 2',
      );
    });

    it('reloads with the status of the tab, from the first page', async () => {
      const fixture = await create(ADMIN);

      click(fixture, '.tab[data-tab="PENDING_APPROVAL"]');

      expect(lastList()).toEqual(jasmine.objectContaining({ status: 'PENDING_APPROVAL', page: 1 }));
      click(fixture, '.tab[data-tab="ALL"]');
      expect(lastList().status).toBeUndefined();
    });

    it('sends every filter to the API', async () => {
      const fixture = await create(ADMIN);

      choose(fixture, '#kpi-filter-keyword', 'thẻ');
      root(fixture)
        .querySelector<HTMLInputElement>('#kpi-filter-keyword')!
        .dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      choose(fixture, 'select[name="period"]', 'q4');
      choose(fixture, 'select[name="position"]', 'p1');
      choose(fixture, 'select[name="credit"]', 'REFERRER');
      choose(fixture, 'select[name="source"]', 'SERVICE_RATING');
      choose(fixture, 'select[name="group"]', 'g1');

      expect(lastList()).toEqual(
        jasmine.objectContaining({
          keyword: 'thẻ',
          effectiveAtPeriodId: 'q4',
          jobPositionId: 'p1',
          creditRole: 'REFERRER',
          dataSource: 'SERVICE_RATING',
          kpiGroupId: 'g1',
        }),
      );
    });

    it('only lists quarters in the period filter', async () => {
      const fixture = await create(ADMIN);

      expect(
        Array.from(root(fixture).querySelectorAll('select[name="period"] option')).map((o) =>
          text(o),
        ),
      ).toEqual(['Hiệu lực tại kỳ: Tất cả', 'Hiệu lực tại kỳ: Q4/2026']);
    });

    it('saves the filters in the browser, brings them back next time and can clear them', async () => {
      let fixture = await create(ADMIN);
      choose(fixture, 'select[name="credit"]', 'REFERRER');

      click(fixture, '#kpi-save-filters');
      expect(text(root(fixture).querySelector('.toast span'))).toBe('Đã lưu bộ lọc.');
      expect(JSON.parse(localStorage.getItem(FILTERS_KEY)!).creditRole).toBe('REFERRER');

      fixture.destroy();
      fixture = await create(ADMIN);
      expect(lastList().creditRole).toBe('REFERRER');

      click(fixture, '#kpi-clear-filters');
      expect(localStorage.getItem(FILTERS_KEY)).toBeNull();
      expect(lastList().creditRole).toBeUndefined();
    });

    it('opens the form to create a KPI, and the next one for the Processor item when asked', async () => {
      const fixture = await create(ADMIN);

      click(fixture, '#kpi-create');
      const form = fixture.debugElement.query((d) => d.name === 'app-kpi-form-modal')
        .componentInstance as FormStub;
      expect(form.mode()).toBe('create');

      const saved = item('KPI-DEP-R', { creditRole: 'REFERRER', productName: 'Tiền gửi' });
      form.saved.emit({ kpi: saved, submitted: false, submitFailed: false, pair: true });
      fixture.detectChanges();

      expect(text(root(fixture).querySelector('.toast span'))).toBe('Đã tạo KPI KPI-DEP-R.');
      const next = fixture.debugElement.query((d) => d.name === 'app-kpi-form-modal')
        .componentInstance as FormStub;
      expect(next.prefill()).toEqual(
        jasmine.objectContaining({
          creditRole: 'PROCESSOR',
          productName: 'Tiền gửi',
          positionIds: ['p1'],
        }),
      );
      expect(api.list).toHaveBeenCalledTimes(2);
    });

    it('tells whether the KPI was also sent, or the sending failed', async () => {
      const fixture = await create(ADMIN);
      const saved = item('KPI-NEW');
      const emit = (result: Partial<KpiFormResult>) => {
        click(fixture, '#kpi-create');
        const form = fixture.debugElement.query((d) => d.name === 'app-kpi-form-modal')
          .componentInstance as FormStub;
        form.saved.emit({
          kpi: saved,
          submitted: false,
          submitFailed: false,
          pair: false,
          ...result,
        });
        fixture.detectChanges();
        return text(root(fixture).querySelector('.toast span'));
      };

      expect(emit({ submitted: true })).toBe('Đã gửi KPI-NEW chờ duyệt.');
      expect(emit({ submitFailed: true })).toBe('Không thực hiện được. Vui lòng thử lại.');
    });

    it('offers the actions each status allows in the row menu', async () => {
      const fixture = await create(ADMIN);
      const menuOf = (index: number) => {
        click(fixture, '.menu-button', index);
        return Array.from(root(fixture).querySelectorAll('.menu button')).map((b) => text(b));
      };

      expect(menuOf(0)).toEqual(['Xem chi tiết', 'Sửa', 'Ngừng hoạt động']);
      expect(menuOf(1)).toEqual(['Xem chi tiết']);
      expect(menuOf(3)).toEqual(['Xem chi tiết', 'Sửa', 'Kích hoạt lại']);
    });

    it('edits a KPI from the menu and sends a draft from the menu', async () => {
      rows = [item('KPI-DRAFT', { status: 'DRAFT' })];
      const fixture = await create(ADMIN);

      click(fixture, '.menu-button');
      click(fixture, '.menu button', 2);
      expect(api.changeStatus).toHaveBeenCalledOnceWith('KPI-DRAFT', 'PENDING_APPROVAL');
      expect(text(root(fixture).querySelector('.toast span'))).toBe('Đã gửi KPI-DRAFT chờ duyệt.');

      click(fixture, '.menu-button');
      click(fixture, '.menu button', 1);
      const form = fixture.debugElement.query((d) => d.name === 'app-kpi-form-modal')
        .componentInstance as FormStub;
      expect(form.mode()).toBe('edit');
      expect(form.kpi()?.code).toBe('KPI-DRAFT');
    });

    it('deactivates and reactivates', async () => {
      const fixture = await create(ADMIN);

      click(fixture, '.menu-button', 0);
      click(fixture, '.menu button', 2);
      click(fixture, '.menu-button', 3);
      click(fixture, '.menu button', 2);

      expect(api.changeStatus.calls.allArgs()).toEqual([
        ['KPI-CARD-P', 'INACTIVE'],
        ['KPI-OLD', 'ACTIVE'],
      ]);
    });

    it('explains an action that fails and reloads when the KPI changed meanwhile', async () => {
      const fixture = await create(ADMIN);
      api.changeStatus.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));

      click(fixture, '.menu-button', 0);
      click(fixture, '.menu button', 2);

      expect(root(fixture).querySelector('.toast')?.classList).toContain('toast-error');
      expect(text(root(fixture).querySelector('.toast span'))).toContain('đã thay đổi');
      expect(api.list).toHaveBeenCalledTimes(2);
    });

    it('opens the drawer on a row, with the System Admin actions', async () => {
      const fixture = await create(ADMIN);

      click(fixture, '.kpi-row');

      const drawer = fixture.debugElement.query((d) => d.name === 'app-kpi-detail-drawer')
        .componentInstance as DrawerStub;
      expect(drawer.kpi().code).toBe('KPI-CARD-P');
      expect(drawer.mode()).toBe('admin');
    });

    it('flags a KPI that is about to end instead of its status', async () => {
      const soon = new Date();
      soon.setDate(soon.getDate() + 15);
      rows = [item('KPI-END', { effectiveTo: period('2026-Q4', soon.toISOString().slice(0, 10)) })];
      const fixture = await create(ADMIN);

      expect(text(root(fixture).querySelector('.kpi-row .chip[data-status]'))).toBe(
        'Sắp hết hiệu lực',
      );
      expect(root(fixture).querySelector('.kpi-row .is-warning')).not.toBeNull();
    });

    it('explains the credit roles under the table', async () => {
      const fixture = await create(ADMIN);

      expect(text(root(fixture).querySelector('.notice'))).toContain(
        'Một giao dịch có Refer ID được tính cho cả hai người',
      );
      expect(root(fixture).querySelector('#kpi-request')).toBeNull();
    });

    it('pages and changes the page size', async () => {
      api.list.and.callFake(() => of({ items: rows, totalCount: 45, page: 1, pageSize: 20 }));
      const fixture = await create(ADMIN);

      expect(text(root(fixture).querySelector('.pager .num'))).toBe('1–20 / 45');
      expect(root(fixture).querySelectorAll('.page-button').length).toBe(3);

      click(fixture, '.page-button', 1);
      expect(lastList()).toEqual(jasmine.objectContaining({ page: 2, pageSize: 20 }));

      pick(fixture, 'select[name="pageSize"]', 1);
      expect(lastList()).toEqual(jasmine.objectContaining({ page: 1, pageSize: 50 }));
    });

    it('says so when nothing matches, and offers a retry when loading fails', async () => {
      rows = [];
      const fixture = await create(ADMIN);
      expect(text(root(fixture).querySelector('.empty-cell'))).toBe('Không có KPI nào phù hợp.');

      api.list.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      click(fixture, '.tab[data-tab="ACTIVE"]');
      expect(text(root(fixture).querySelector('.state-error'))).toContain(
        'Không tải được danh sách KPI',
      );

      api.list.and.callFake(() => of({ items: [item('A')], totalCount: 1, page: 1, pageSize: 20 }));
      click(fixture, '.state-error button');
      expect(root(fixture).querySelector('.kpi-row')).not.toBeNull();
    });
  });

  describe('as the Branch Manager', () => {
    it('opens on the KPIs awaiting approval and cannot create one', async () => {
      const fixture = await create(MANAGER);

      expect(lastList().status).toBe('PENDING_APPROVAL');
      expect(text(root(fixture).querySelector('.tab.is-active'))).toBe('Chờ duyệt · 2');
      expect(root(fixture).querySelector('#kpi-create')).toBeNull();
      expect(text(root(fixture).querySelector('#kpi-request'))).toBe('Yêu cầu KPI mới');
      expect(text(root(fixture).querySelector('.breadcrumb'))).toContain('Chỉ tiêu & chiến dịch');
    });

    it('shows when each KPI was sent and Reject / Approve only on a pending one', async () => {
      rows = [rows[1], rows[2]];
      const fixture = await create(MANAGER);

      expect(Array.from(root(fixture).querySelectorAll('thead th')).map((th) => text(th))).toEqual([
        'MÃ',
        'TÊN KPI',
        'VỊ TRÍ',
        'CREDIT',
        'TỔNG HỢP',
        'HIỆU LỰC',
        'GỬI LÚC',
        'THAO TÁC',
      ]);
      const pending = root(fixture).querySelectorAll('.kpi-row')[0];
      expect(text(pending)).toContain('22/10/2026');
      expect(Array.from(pending.querySelectorAll('button')).map((b) => text(b))).toEqual([
        'Từ chối',
        'Duyệt',
      ]);
      expect(root(fixture).querySelectorAll('.kpi-row')[1].querySelectorAll('button').length).toBe(
        0,
      );
      expect(text(root(fixture).querySelector('.pager .num'))).toContain('2 KPI chờ duyệt');
    });

    it('has fewer filters', async () => {
      const fixture = await create(MANAGER);

      expect(root(fixture).querySelector('select[name="period"]')).toBeNull();
      expect(root(fixture).querySelector('select[name="source"]')).toBeNull();
      expect(root(fixture).querySelector('#kpi-save-filters')).toBeNull();
      expect(root(fixture).querySelector('.notice')).toBeNull();
    });

    it('opens the decision with the button that was pressed, then reloads and says what was decided', async () => {
      const fixture = await create(MANAGER);

      click(fixture, '.reject-button');
      let decision = fixture.debugElement.query((d) => d.name === 'app-kpi-decision-modal')
        .componentInstance as DecisionStub;
      expect([decision.kpi().code, decision.initial()]).toEqual(['KPI-CARD-R', 'REJECT']);

      decision.decided.emit({ decision: 'REJECT', kpi: decision.kpi() });
      fixture.detectChanges();
      expect(text(root(fixture).querySelector('.toast span'))).toBe('Đã từ chối KPI-CARD-R.');
      expect(fixture.debugElement.query((d) => d.name === 'app-kpi-decision-modal')).toBeNull();
      expect(api.list).toHaveBeenCalledTimes(2);

      click(fixture, '.approve-button');
      decision = fixture.debugElement.query((d) => d.name === 'app-kpi-decision-modal')
        .componentInstance as DecisionStub;
      expect(decision.initial()).toBe('APPROVE');
      decision.decided.emit({ decision: 'APPROVE', kpi: decision.kpi() });
      fixture.detectChanges();
      expect(text(root(fixture).querySelector('.toast span'))).toBe('Đã duyệt KPI-CARD-R.');
    });

    it('lets the drawer start a decision, and requests a new KPI', async () => {
      const fixture = await create(MANAGER);

      click(fixture, '.kpi-row', 1);
      const drawer = fixture.debugElement.query((d) => d.name === 'app-kpi-detail-drawer')
        .componentInstance as DrawerStub;
      expect(drawer.mode()).toBe('approver');
      drawer.decide.emit('APPROVE');
      fixture.detectChanges();
      expect(fixture.debugElement.query((d) => d.name === 'app-kpi-decision-modal')).not.toBeNull();

      click(fixture, '#kpi-request');
      const form = fixture.debugElement.query((d) => d.name === 'app-kpi-form-modal')
        .componentInstance as FormStub;
      expect([form.mode(), form.canSubmit()]).toEqual(['request', false]);
      form.saved.emit({ kpi: item('NEW'), submitted: false, submitFailed: false, pair: false });
      fixture.detectChanges();
      expect(text(root(fixture).querySelector('.toast span'))).toBe('Đã gửi yêu cầu KPI NEW.');
    });
  });

  it('is read only for someone who can neither manage nor approve', async () => {
    const fixture = await create(['KPI_READ']);

    expect(root(fixture).querySelector('#kpi-create')).toBeNull();
    expect(root(fixture).querySelector('#kpi-request')).toBeNull();
    expect(root(fixture).querySelector('.menu-button')).toBeNull();
    click(fixture, '.kpi-row');
    const drawer = fixture.debugElement.query((d) => d.name === 'app-kpi-detail-drawer')
      .componentInstance as DrawerStub;
    expect(drawer.mode()).toBe('viewer');
  });
});
