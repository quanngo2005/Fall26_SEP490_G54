import { ComponentRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { KpiDefinition, KpiHistoryEntry, KpiUsage } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { KpiDetailDrawerComponent, KpiViewerMode } from './kpi-detail-drawer.component';

const kpi = (extra: Partial<KpiDefinition> = {}): KpiDefinition =>
  ({
    id: 'k1',
    code: 'KPI-CARD-R',
    name: 'Mở thẻ – giới thiệu',
    unit: 'hợp đồng',
    direction: 'HIGHER_BETTER',
    aggregationType: 'SUM',
    dataSource: 'TRANSACTION',
    creditRole: 'REFERRER',
    productName: 'Thẻ / E-banking',
    status: 'PENDING_APPROVAL',
    formula: 'COUNT(*) WHERE Sản phẩm = Thẻ AND Refer ID = Nhân viên',
    maxAchievementPercent: null,
    baselineValue: null,
    jobPositions: [{ id: 'p1', code: 'TELLER', title: 'Teller' }],
    effectiveFrom: {
      id: 'q1',
      code: '2027-Q1',
      name: 'Quý 1/2027',
      startDate: '2027-01-01',
      endDate: '2027-03-31',
    },
    effectiveTo: null,
    submittedAt: '2026-10-22T10:12:40+07:00',
    rejectReason: null,
    requestNote: null,
    ...extra,
  }) as KpiDefinition;

const USAGE: KpiUsage = {
  objectives: [{ id: 'o1', code: 'OBJ-2026-02' }],
  activeTargetCount: 0,
  relatedItems: [
    {
      id: 'p',
      code: 'KPI-CARD-P',
      name: 'Mở thẻ – xử lý',
      creditRole: 'PROCESSOR',
      dataSource: 'TRANSACTION',
      jobPositions: ['TELLER', 'SALES'],
    },
    {
      id: 'k1',
      code: 'KPI-CARD-R',
      name: 'Mở thẻ – giới thiệu',
      creditRole: 'REFERRER',
      dataSource: 'TRANSACTION',
      jobPositions: ['TELLER'],
    },
  ],
};

const HISTORY: KpiHistoryEntry[] = [
  {
    occurredAt: '2026-10-22T03:12:00+00:00',
    actorName: 'Nguyễn Văn An',
    action: 'CREATE',
    summary: 'Tạo',
  },
  {
    occurredAt: '2026-10-22T03:12:40+00:00',
    actorName: 'Nguyễn Văn An',
    action: 'SUBMIT',
    summary: 'Gửi duyệt',
  },
];

describe('KpiDetailDrawerComponent', () => {
  const api = jasmine.createSpyObj<KpiDefinitionService>('KpiDefinitionService', [
    'usage',
    'history',
  ]);
  let fixture: ComponentFixture<KpiDetailDrawerComponent>;
  let element: HTMLElement;

  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  async function create(
    item: KpiDefinition = kpi(),
    mode: KpiViewerMode = 'approver',
  ): Promise<void> {
    api.usage.and.returnValue(of(USAGE));
    api.history.and.returnValue(of(HISTORY));
    await TestBed.configureTestingModule({
      imports: [KpiDetailDrawerComponent],
      providers: [{ provide: KpiDefinitionService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    fixture = TestBed.createComponent(KpiDetailDrawerComponent);
    const ref: ComponentRef<KpiDetailDrawerComponent> = fixture.componentRef;
    ref.setInput('kpi', item);
    ref.setInput('mode', mode);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('shows the definition, who sent the KPI and when', async () => {
    await create();

    expect(text(element.querySelector('h2'))).toBe('KPI-CARD-R · Mở thẻ – giới thiệu');
    expect(text(element.querySelector('.drawer-head p'))).toContain('Nguyễn Văn An gửi lúc');
    expect(text(element.querySelector('.chip'))).toBe('Chờ duyệt');
    const facts = text(element.querySelector('.facts'));
    expect(facts).toContain('hợp đồng · Càng cao càng tốt');
    expect(facts).toContain('Core Banking – Transaction');
    expect(facts).toContain('Teller');
    expect(facts).toContain('Q1/2027 → không hạn');
    expect(text(element.querySelector('.formula'))).toContain('Refer ID = Nhân viên');
  });

  it('lists the KPI items of the product with where each takes its credit from, the current one marked', async () => {
    await create();

    expect(text(element.querySelector('.drawer-body h3:nth-of-type(2)'))).toBe(
      'KPI ITEM THEO VAI TRÒ CREDIT · THẺ / E-BANKING',
    );
    const rows = Array.from(element.querySelectorAll('.related tbody tr'));
    expect(
      rows.map((row) =>
        Array.from(row.querySelectorAll('th, td'))
          .map((cell) => text(cell))
          .join(' '),
      ),
    ).toEqual([
      'KPI-CARD-P TELLER, SALES Xử lý Mã NV trên dòng GD',
      'KPI-CARD-R TELLER Giới thiệu Cột Refer ID',
    ]);
    expect(rows[1].classList).toContain('is-current');
  });

  it('shows where the KPI is used and says allocation is not there yet for a KPI not in force', async () => {
    await create();

    const usage = text(element.querySelectorAll('.facts')[1]);
    expect(usage).toContain('OBJ-2026-02');
    expect(usage).toContain('Chưa có (KPI chưa hiệu lực)');
  });

  it('counts active targets once the KPI is in force', async () => {
    api.usage.and.returnValue(of({ ...USAGE, activeTargetCount: 12 }));
    await create(kpi({ status: 'ACTIVE' }));
    api.usage.and.returnValue(of({ ...USAGE, activeTargetCount: 12 }));
    fixture.componentRef.setInput('kpi', kpi({ status: 'ACTIVE', id: 'k2' }));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(element.querySelectorAll('.facts')[1])).toContain('12 mục tiêu đang áp dụng');
  });

  it('lists the history oldest first', async () => {
    await create();

    expect(
      Array.from(element.querySelectorAll('.history li')).map((li) =>
        text(li).replace(/^\S+ \S+ · /, ''),
      ),
    ).toEqual(['Nguyễn Văn An · Tạo', 'Nguyễn Văn An · Gửi duyệt']);
  });

  it('gives the Branch Manager Approve and Reject on a pending KPI', async () => {
    await create();
    const decisions: string[] = [];
    fixture.componentInstance.decide.subscribe((value) => decisions.push(value));

    element.querySelector<HTMLElement>('#kpi-drawer-reject')?.click();
    element.querySelector<HTMLElement>('#kpi-drawer-approve')?.click();

    expect(decisions).toEqual(['REJECT', 'APPROVE']);
    expect(element.querySelector('#kpi-drawer-edit')).toBeNull();
  });

  it('gives the System Admin Edit and Send, but only where the status allows', async () => {
    await create(kpi({ status: 'DRAFT' }), 'admin');
    let edited = 0;
    let sent = 0;
    fixture.componentInstance.edit.subscribe(() => edited++);
    fixture.componentInstance.submitForApproval.subscribe(() => sent++);

    element.querySelector<HTMLElement>('#kpi-drawer-edit')?.click();
    element.querySelector<HTMLElement>('#kpi-drawer-submit')?.click();

    expect([edited, sent]).toEqual([1, 1]);
    expect(element.querySelector('#kpi-drawer-approve')).toBeNull();
  });

  it('shows no actions to the System Admin for a pending KPI, nor to a viewer', async () => {
    await create(kpi({ status: 'PENDING_APPROVAL' }), 'admin');
    expect(element.querySelectorAll('.drawer-foot button').length).toBe(0);

    fixture.destroy();
    TestBed.resetTestingModule();
    await create(kpi({ status: 'ACTIVE' }), 'viewer');
    expect(element.querySelectorAll('.drawer-foot button').length).toBe(0);
  });

  it('shows the reason a KPI was rejected and the request it came from', async () => {
    await create(
      kpi({ status: 'REJECTED', rejectReason: 'Thiếu công thức', requestNote: 'Cần cho thẻ' }),
      'admin',
    );

    const callouts = Array.from(element.querySelectorAll('.callout')).map((c) => text(c));
    expect(callouts).toEqual([
      'Lý do từ chối Thiếu công thức',
      'Yêu cầu của Giám đốc chi nhánh Cần cho thẻ',
    ]);
  });

  it('flags a KPI that is about to end', async () => {
    const soon = new Date();
    soon.setDate(soon.getDate() + 10);
    const end = soon.toISOString().slice(0, 10);
    await create(
      kpi({
        status: 'ACTIVE',
        effectiveTo: {
          id: 'q',
          code: '2026-Q4',
          name: 'Q4',
          startDate: '2026-10-01',
          endDate: end,
        },
      }),
    );

    expect(
      Array.from(element.querySelectorAll('.drawer-head .chip')).map((c) => text(c)),
    ).toContain('Sắp hết hiệu lực');
  });

  it('still shows the definition when usage and history cannot be loaded', async () => {
    api.usage.and.returnValue(throwError(() => new Error('down')));
    api.history.and.returnValue(throwError(() => new Error('down')));
    await TestBed.configureTestingModule({
      imports: [KpiDetailDrawerComponent],
      providers: [{ provide: KpiDefinitionService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    fixture = TestBed.createComponent(KpiDetailDrawerComponent);
    fixture.componentRef.setInput('kpi', kpi());
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(element.querySelector('.facts'))).toContain('Core Banking – Transaction');
    expect(text(element.querySelector('.history'))).toBe('Chưa có thay đổi.');
  });

  it('closes on Escape', async () => {
    await create();
    let closed = false;
    fixture.componentInstance.closed.subscribe(() => (closed = true));

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(closed).toBeTrue();
  });
});
