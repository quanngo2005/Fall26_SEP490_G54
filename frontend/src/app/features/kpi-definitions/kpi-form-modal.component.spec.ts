import { HttpErrorResponse } from '@angular/common/http';
import { ComponentRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { JobPositionSummary } from '../../core/job-position.models';
import { KpiDefinition, KpiGroupOption } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import {
  KpiFormMode,
  KpiFormModalComponent,
  KpiFormResult,
  KpiFormValues,
} from './kpi-form-modal.component';

const GROUPS = [{ id: 'g1', code: 'KD', name: 'Sales' }] as KpiGroupOption[];
const POSITIONS = [
  { id: 'p1', code: 'TELLER', title: 'Teller' },
  { id: 'p2', code: 'SALES', title: 'Sales Officer' },
] as JobPositionSummary[];
const PERIODS = [
  {
    id: 'q4',
    code: '2026-Q4',
    name: 'Quý 4/2026',
    periodType: 'QUARTER',
    startDate: '2026-10-01',
    endDate: '2026-12-31',
  },
  {
    id: 'q1',
    code: '2027-Q1',
    name: 'Quý 1/2027',
    periodType: 'QUARTER',
    startDate: '2027-01-01',
    endDate: '2027-03-31',
  },
] as EvaluationPeriodOption[];

const SAVED = { id: 'new', code: 'TIEN_GUI_GIOI_THIEU', status: 'DRAFT' } as KpiDefinition;

const EXISTING = {
  id: 'k1',
  code: 'KPI-OLD',
  name: 'Cũ',
  kpiGroupId: 'g1',
  description: null,
  formula: null,
  unit: 'VND',
  aggregationType: 'SUM',
  direction: 'HIGHER_BETTER',
  maxAchievementPercent: 150,
  baselineValue: null,
  status: 'DRAFT',
  creditRole: 'REFERRER',
  dataSource: 'TRANSACTION',
  productName: 'Thẻ',
  jobPositions: [{ id: 'p1', code: 'TELLER', title: 'Teller' }],
  effectiveFrom: { id: 'q4' },
  effectiveTo: null,
} as unknown as KpiDefinition;

describe('KpiFormModalComponent', () => {
  const api = jasmine.createSpyObj<KpiDefinitionService>('KpiDefinitionService', [
    'create',
    'update',
    'request',
    'changeStatus',
    'codeSuggestion',
  ]);
  let fixture: ComponentFixture<KpiFormModalComponent>;
  let element: HTMLElement;
  let results: KpiFormResult[];

  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  async function create(
    mode: KpiFormMode,
    options: { kpi?: KpiDefinition; prefill?: Partial<KpiFormValues>; canSubmit?: boolean } = {},
  ): Promise<void> {
    for (const spy of Object.values(api)) (spy as jasmine.Spy).calls.reset();
    api.create.and.returnValue(of(SAVED));
    api.update.and.returnValue(of(SAVED));
    api.request.and.returnValue(of(SAVED));
    api.changeStatus.and.returnValue(of(undefined));
    api.codeSuggestion.and.callFake((name: string) =>
      of(name.startsWith('Tiền gửi') ? 'TIEN_GUI_GIOI_THIEU' : 'KPI_MOI'),
    );
    results = [];
    await TestBed.configureTestingModule({
      imports: [KpiFormModalComponent],
      providers: [{ provide: KpiDefinitionService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    fixture = TestBed.createComponent(KpiFormModalComponent);
    const ref: ComponentRef<KpiFormModalComponent> = fixture.componentRef;
    ref.setInput('mode', mode);
    ref.setInput('kpi', options.kpi ?? null);
    ref.setInput('groups', GROUPS);
    ref.setInput('positions', POSITIONS);
    ref.setInput('periods', PERIODS);
    ref.setInput('products', ['Thẻ']);
    ref.setInput('units', ['VND']);
    ref.setInput('prefill', options.prefill ?? null);
    ref.setInput('canSubmit', options.canSubmit ?? true);
    fixture.componentInstance.saved.subscribe((result) => results.push(result));
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const set = async (selector: string, value: string) => {
    const field = element.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(
      selector,
    )!;
    field.value = value;
    field.dispatchEvent(new Event(field instanceof HTMLSelectElement ? 'change' : 'input'));
    fixture.detectChanges();
    await fixture.whenStable();
  };

  const click = (selector: string) => {
    element.querySelector<HTMLElement>(selector)?.click();
    fixture.detectChanges();
  };

  /** Waits (up to two seconds) until `check` holds, redrawing the form between looks. */
  const until = async (check: () => boolean) => {
    for (let look = 0; look < 40 && !check(); look++) {
      await new Promise((resolve) => setTimeout(resolve, 50));
      fixture.detectChanges();
    }
  };

  const codeField = () => element.querySelector<HTMLInputElement>('#kpi-code')!;

  /** The code is asked for once the user stops typing: wait until the field shows it (or is empty again). */
  const typeName = async (name: string) => {
    await set('#kpi-name', name);
    const wanted =
      name.trim() === '' ? () => codeField().value === '' : () => codeField().value !== '';
    await until(wanted);
    fixture.detectChanges();
  };

  /** Clicks a radio or checkbox and lets ngModel write the model before the form is read again. */
  const choose = async (selector: string, index = 0) => {
    element.querySelectorAll<HTMLElement>(selector)[index]?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  const fillValid = async () => {
    await set('#kpi-group', 'g1');
    await typeName('Tiền gửi – giới thiệu');
    click('input[name="position-p1"]');
    await set('input[name="unit"]', 'VND');
    await set('#kpi-from', 'q1');
    await fixture.whenStable();
  };

  describe('create', () => {
    it('lists the three sections, the groups, positions and quarters', async () => {
      await create('create');

      expect(Array.from(element.querySelectorAll('h3.section')).map((h) => text(h))).toEqual([
        '1. ĐỊNH DANH',
        '2. CÁCH ĐO',
        '3. HIỆU LỰC & GIỚI HẠN',
      ]);
      expect(text(element.querySelector('h2'))).toBe('Tạo KPI');
      expect(Array.from(element.querySelectorAll('#kpi-group option')).map((o) => text(o))).toEqual(
        ['Chọn nhóm', 'Sales'],
      );
      expect(element.querySelectorAll('input[type="checkbox"][name^="position-"]').length).toBe(2);
      expect(Array.from(element.querySelectorAll('#kpi-from option')).map((o) => text(o))).toEqual([
        'Chọn kỳ',
        'Q4/2026',
        'Q1/2027',
      ]);
    });

    it('shows the code as a read-only field that is filled from the name', async () => {
      await create('create');
      const code = codeField;

      expect(code().readOnly).toBeTrue();
      expect(code().value).toBe('');
      expect(code().placeholder).toBe('Tự sinh sau khi nhập tên KPI');
      expect(text(element.querySelector('#kpi-code + .hint'))).toBe(
        'Hệ thống tự sinh mã từ tên KPI, không sửa được.',
      );

      await typeName('Tiền gửi – giới thiệu');
      expect(api.codeSuggestion).toHaveBeenCalledOnceWith('Tiền gửi – giới thiệu');
      expect(code().value).toBe('TIEN_GUI_GIOI_THIEU');

      await typeName('   ');
      expect(code().value).toBe('');
    });

    it('asks once for a name typed quickly, and leaves the code empty when the server cannot suggest one', async () => {
      await create('create');
      api.codeSuggestion.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      await typeName('Mở thẻ');

      expect(api.codeSuggestion).toHaveBeenCalledTimes(1);
      expect(element.querySelector<HTMLInputElement>('#kpi-code')?.value).toBe('');
    });

    it('has no English name field', async () => {
      await create('create');

      expect(element.querySelector('#kpi-name-en')).toBeNull();
      expect(element.querySelector('input[name="nameEn"]')).toBeNull();
    });

    it('refuses to save until the required fields are filled', async () => {
      await create('create');

      click('#kpi-save-submit');

      expect(api.create).not.toHaveBeenCalled();
      expect(element.querySelectorAll('.field-error').length).toBeGreaterThanOrEqual(5);
    });

    it('saves a draft with every catalog field', async () => {
      await create('create');
      await fillValid();

      click('#kpi-save-draft');

      expect(api.create).toHaveBeenCalledOnceWith(
        jasmine.objectContaining({
          code: 'TIEN_GUI_GIOI_THIEU',
          kpiGroupId: 'g1',
          name: 'Tiền gửi – giới thiệu',
          jobPositionIds: ['p1'],
          creditRole: 'PROCESSOR',
          dataSource: 'TRANSACTION',
          productName: null,
          unit: 'VND',
          aggregationType: 'SUM',
          direction: 'HIGHER_BETTER',
          effectiveFromPeriodId: 'q1',
          effectiveToPeriodId: null,
          maxAchievementPercent: null,
          baselineValue: null,
        }),
      );
      expect(api.changeStatus).not.toHaveBeenCalled();
      expect(results).toEqual([{ kpi: SAVED, submitted: false, submitFailed: false, pair: false }]);
    });

    it('saves and sends for approval', async () => {
      await create('create');
      await fillValid();

      click('#kpi-save-submit');

      expect(api.changeStatus).toHaveBeenCalledOnceWith('new', 'PENDING_APPROVAL');
      expect(results[0].submitted).toBeTrue();
    });

    it('keeps the KPI as a draft and says so when sending it for approval fails', async () => {
      await create('create');
      api.changeStatus.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
      await fillValid();

      click('#kpi-save-submit');

      expect(results).toEqual([{ kpi: SAVED, submitted: false, submitFailed: true, pair: false }]);
    });

    it('saves with Ctrl+S', async () => {
      await create('create');
      await fillValid();

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true }));

      expect(api.create).toHaveBeenCalled();
      expect(api.changeStatus).toHaveBeenCalled();
    });

    it('rejects a last period before the first one (MSG27)', async () => {
      await create('create');
      await fillValid();
      await set('#kpi-to', 'q4');

      click('#kpi-save-draft');

      expect(api.create).not.toHaveBeenCalled();
      expect(text(element.querySelector('#kpi-to + .field-error'))).toContain('MSG27');
    });

    it('rejects a bad ceiling or baseline', async () => {
      await create('create');
      await fillValid();
      await set('#kpi-ceiling', '-3');
      await set('#kpi-baseline', 'abc');

      click('#kpi-save-draft');

      expect(api.create).not.toHaveBeenCalled();
      expect(element.querySelectorAll('.more .field-error').length).toBe(2);
    });

    it('offers to create the Processor item only for a Referrer item, and says so in the result', async () => {
      await create('create');
      expect(element.querySelector('input[name="pair"]')).toBeNull();

      await choose('input[name="creditRole"]', 1);
      expect(text(element.querySelector('.hint-line'))).toContain('Tạo luôn item Xử lý');
      await choose('input[name="pair"]');
      await fillValid();
      click('#kpi-save-draft');

      expect(api.create).toHaveBeenCalledWith(jasmine.objectContaining({ creditRole: 'REFERRER' }));
      expect(results[0].pair).toBeTrue();
    });

    it('starts from the prefill', async () => {
      await create('create', {
        prefill: {
          creditRole: 'PROCESSOR',
          productName: 'Thẻ',
          positionIds: ['p2'],
          fromPeriodId: 'q4',
        },
      });

      expect(element.querySelector<HTMLInputElement>('#kpi-product')?.value).toBe('Thẻ');
      expect(
        element.querySelector<HTMLInputElement>('input[name="position-p2"]')?.checked,
      ).toBeTrue();
      expect(element.querySelector<HTMLSelectElement>('#kpi-from')?.value).toBe('q4');
    });

    it('shows a duplicate code under the code field, a locked period under the period, and any other error at the bottom', async () => {
      await create('create');
      await fillValid();
      const fail = (status: number, message = '') => {
        api.create.and.returnValue(
          throwError(() => new HttpErrorResponse({ status, error: { message } })),
        );
        click('#kpi-save-draft');
      };

      fail(409);
      expect(text(element.querySelector('#kpi-code + .field-error'))).toBe('Mã KPI đã tồn tại.');
      fail(400, 'Không chọn kỳ đã khóa (MSG27).');
      expect(text(element.querySelector('#kpi-to + .field-error'))).toContain('MSG27');
      fail(400, 'Có vị trí áp dụng không tồn tại.');
      expect(text(element.querySelector('.form-error'))).toBe('Có vị trí áp dụng không tồn tại.');
      fail(500);
      expect(text(element.querySelector('.form-error'))).toContain('Không thực hiện được');
    });
  });

  describe('edit', () => {
    it('loads the KPI, locks the code and updates it', async () => {
      await create('edit', { kpi: EXISTING });
      await typeName('Tên khác');
      expect(api.codeSuggestion).not.toHaveBeenCalled();

      expect(text(element.querySelector('h2'))).toBe('Sửa KPI');
      expect(element.querySelector<HTMLInputElement>('#kpi-code')?.readOnly).toBeTrue();
      expect(element.querySelector<HTMLInputElement>('#kpi-code')?.value).toBe('KPI-OLD');
      expect(
        element.querySelector<HTMLInputElement>('input[name="position-p1"]')?.checked,
      ).toBeTrue();
      expect(element.querySelector<HTMLInputElement>('#kpi-ceiling')?.value).toBe('150');

      click('#kpi-save-draft');

      expect(api.update).toHaveBeenCalledOnceWith(
        'k1',
        jasmine.objectContaining({
          code: 'KPI-OLD',
          creditRole: 'REFERRER',
          jobPositionIds: ['p1'],
        }),
      );
      expect(api.create).not.toHaveBeenCalled();
    });

    it('only offers a plain save for a KPI that is already in force', async () => {
      await create('edit', { kpi: { ...EXISTING, status: 'ACTIVE' } });

      expect(element.querySelector('#kpi-save-draft')).toBeNull();
      expect(element.querySelector('#kpi-save-submit')).toBeNull();

      click('#kpi-save');

      expect(api.update).toHaveBeenCalled();
      expect(api.changeStatus).not.toHaveBeenCalled();
    });

    it('does not offer to send for approval when the user cannot', async () => {
      await create('edit', { kpi: EXISTING, canSubmit: false });

      expect(element.querySelector('#kpi-save-submit')).toBeNull();
      expect(element.querySelector('#kpi-save')).not.toBeNull();
    });
  });

  describe('request', () => {
    it('needs a reason and sends the KPI with it', async () => {
      await create('request');
      await fillValid();

      click('button[type="submit"]');
      expect(api.request).not.toHaveBeenCalled();
      expect(text(element.querySelector('#kpi-request-note + .field-error'))).toBe(
        'Trường này là bắt buộc.',
      );

      await set('#kpi-request-note', '  Cần cho sản phẩm thẻ  ');
      click('button[type="submit"]');

      expect(api.request).toHaveBeenCalledOnceWith({
        kpi: jasmine.objectContaining({ code: 'TIEN_GUI_GIOI_THIEU' }),
        requestNote: 'Cần cho sản phẩm thẻ',
      });
      expect(api.create).not.toHaveBeenCalled();
      expect(text(element.querySelector('h2'))).toBe('Yêu cầu KPI mới');
    });
  });

  it('closes on Escape and from the close button', async () => {
    await create('create');
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    click('.dialog-close');

    expect(closed).toBe(2);
  });
});
