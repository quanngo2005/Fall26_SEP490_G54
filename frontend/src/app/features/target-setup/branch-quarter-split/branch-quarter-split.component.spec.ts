import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { I18nService } from '../../../core/i18n/i18n.service';
import { QuarterSplit, QuarterSplitRow } from '../../../core/target-assignment.models';
import { TargetAssignmentService } from '../../../core/target-assignment.service';
import { BranchQuarterSplitComponent } from './branch-quarter-split.component';

const YEAR = 2026;

function kpiRow(over: Partial<QuarterSplitRow> = {}): QuarterSplitRow {
  return {
    kpiId: 'k1',
    kpiCode: 'KPI_1',
    kpiName: 'KPI 1',
    unit: 'VND',
    isRatio: false,
    yearValue: 100,
    q1: 25,
    q2: 25,
    q3: 25,
    q4: 25,
    previousYear: null,
    sum: 100,
    diff: 0,
    matched: true,
    ...over,
  };
}

const ratioRow = kpiRow({
  kpiId: 'k3',
  kpiCode: 'KPI_3',
  kpiName: 'Tỷ lệ nợ xấu',
  unit: '%',
  isRatio: true,
  yearValue: 0.5,
  q1: 0.5,
  q2: 0.5,
  q3: 0.5,
  q4: 0.5,
  sum: 2,
  diff: -1.5,
  matched: false,
});

function splitState(over: Partial<QuarterSplit> = {}): QuarterSplit {
  return {
    branchYearId: 'by-1',
    year: YEAR,
    status: 'DRAFT',
    importedAt: null,
    fileRatios: null,
    quarters: [1, 2, 3, 4].map((quarter) => ({
      periodId: `p${quarter}`,
      quarter,
      locked: false,
    })),
    rows: [
      kpiRow(),
      kpiRow({
        kpiId: 'k2',
        kpiCode: 'KPI_2',
        kpiName: 'KPI 2',
        unit: 'KH',
        yearValue: 10,
        q1: 1,
        q2: 2,
        q3: 3,
        q4: 4,
        previousYear: { q1: 1, q2: 2, q3: 3, q4: 4 },
        sum: 10,
      }),
      ratioRow,
    ],
    ...over,
  };
}

describe('BranchQuarterSplitComponent', () => {
  let api: jasmine.SpyObj<TargetAssignmentService>;
  let state: QuarterSplit;

  function render(
    over: Partial<QuarterSplit> = {},
    options: { load?: 'ok' | 'error'; save?: 'ok' | 'locked' } = {},
  ): ComponentFixture<BranchQuarterSplitComponent> {
    state = splitState(over);
    api = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'getQuarterSplit',
      'saveQuarterSplit',
    ]);
    api.getQuarterSplit.and.returnValue(
      options.load === 'error' ? throwError(() => new Error('boom')) : of(state),
    );
    api.saveQuarterSplit.and.returnValue(
      options.save === 'locked'
        ? throwError(
            () =>
              new HttpErrorResponse({
                status: 409,
                error: {
                  success: false,
                  status: 409,
                  data: null,
                  errorCode: 'PERIOD_LOCKED',
                },
              }),
          )
        : of(splitState()),
    );

    TestBed.configureTestingModule({
      providers: [{ provide: TargetAssignmentService, useValue: api }],
    });
    TestBed.inject(I18nService).setLanguage('vi');

    const fixture = TestBed.createComponent(BranchQuarterSplitComponent);
    fixture.componentRef.setInput('branchYearId', 'by-1');
    fixture.componentRef.setInput('year', YEAR);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ComponentFixture<BranchQuarterSplitComponent>) =>
    fixture.nativeElement as HTMLElement;

  afterEach(() => localStorage.clear());

  function buttonByText(
    fixture: ComponentFixture<BranchQuarterSplitComponent>,
    label: string,
  ): HTMLButtonElement {
    const button = Array.from(root(fixture).querySelectorAll<HTMLButtonElement>('button')).find(
      (item) => item.textContent?.trim() === label,
    );
    if (!button) {
      throw new Error(`Button "${label}" not found`);
    }
    return button;
  }

  function dataRows(fixture: ComponentFixture<BranchQuarterSplitComponent>): HTMLTableRowElement[] {
    return Array.from(root(fixture).querySelectorAll<HTMLTableRowElement>('.quarter-row'));
  }

  function quarterInputs(
    fixture: ComponentFixture<BranchQuarterSplitComponent>,
  ): HTMLInputElement[] {
    return Array.from(root(fixture).querySelectorAll<HTMLInputElement>('.quarter-input'));
  }

  function type(
    fixture: ComponentFixture<BranchQuarterSplitComponent>,
    input: HTMLInputElement,
    value: string,
  ): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('renders the grid header, rows, units and the read-only yearly value', () => {
    const fixture = render();

    const headers = Array.from(root(fixture).querySelectorAll('thead th')).map((th) =>
      th.textContent?.trim(),
    );
    expect(headers.slice(0, 8)).toEqual([
      'CHỈ TIÊU',
      'ĐƠN VỊ',
      'CẢ NĂM (FILE)',
      'NĂM (TỔNG 4 QUÝ)',
      'QUÝ 1',
      'QUÝ 2',
      'QUÝ 3',
      'QUÝ 4',
    ]);

    const rows = dataRows(fixture);
    expect(rows.length).toBe(3);
    expect(rows[0].querySelector('.kpi-cell')?.textContent?.trim()).toBe('KPI 1');
    expect(rows[0].querySelector('.unit-cell')?.textContent?.trim()).toBe('VND');
    expect(rows[0].querySelector('.year-cell')?.textContent).toContain('100');
    expect(rows[0].querySelector('.year-lock')).not.toBeNull();
    expect(rows[0].querySelector('.total-cell')?.textContent?.trim()).toBe('100 ✓');
  });

  it('splits evenly with the rounding remainder on the last quarter', () => {
    const fixture = render();

    buttonByText(fixture, 'Chia đều 4 quý').click();
    fixture.detectChanges();

    const inputs = quarterInputs(fixture);
    expect(inputs.slice(0, 4).map((input) => input.value)).toEqual(['25', '25', '25', '25']);
    expect(inputs.slice(4, 8).map((input) => input.value)).toEqual(['2', '2', '2', '4']);

    const totals = Array.from(root(fixture).querySelectorAll('.total-cell')).map((cell) =>
      cell.textContent?.trim(),
    );
    expect(totals).toEqual(['100 ✓', '10 ✓', '—']);
    expect(root(fixture).querySelector('.quarter-banner')).toBeNull();
  });

  it('keeps a locked quarter untouched while filling the others', () => {
    const fixture = render({
      quarters: [1, 2, 3, 4].map((quarter) => ({
        periodId: `p${quarter}`,
        quarter,
        locked: quarter === 1,
      })),
      rows: [
        kpiRow(),
        kpiRow({
          kpiId: 'k2',
          kpiName: 'KPI 2',
          yearValue: 10,
          q1: 7,
          q2: 1,
          q3: 1,
          q4: 1,
          sum: 10,
        }),
        ratioRow,
      ],
    });

    buttonByText(fixture, 'Chia đều 4 quý').click();
    fixture.detectChanges();

    const inputs = quarterInputs(fixture);
    expect(inputs[4].disabled).toBeTrue();
    expect(inputs[4].value).toBe('7');
    expect(inputs.slice(5, 8).map((input) => input.value)).toEqual(['1', '1', '1']);
    expect(dataRows(fixture)[1].querySelector('.total-cell')?.textContent?.trim()).toBe('10 ✓');
  });

  it('marks the row total red, the edited input red and lists the mismatch', () => {
    const fixture = render();

    type(fixture, quarterInputs(fixture)[0], '10');

    const row = dataRows(fixture)[0];
    expect(row.querySelector('.total-cell')?.classList).toContain('is-mismatch');
    expect(row.querySelector('.total-cell')?.textContent?.trim()).toBe('85 · thiếu 15');
    expect(quarterInputs(fixture)[0].classList).toContain('is-edited-error');

    const banner = root(fixture).querySelector('.quarter-banner');
    expect(banner?.querySelector('.banner-line')?.textContent?.trim()).toBe(
      '"KPI 1": tổng 4 quý là 85, chỉ tiêu năm là 100 (thiếu 15)',
    );
    expect(banner?.querySelector('.banner-footer')?.textContent?.trim()).toBe(
      'Chưa phân bổ cho phòng được.',
    );
  });

  it('reports an excess total with "thừa"', () => {
    const fixture = render();

    type(fixture, quarterInputs(fixture)[0], '120');

    expect(dataRows(fixture)[0].querySelector('.total-cell')?.textContent?.trim()).toBe(
      '195 · thừa 95',
    );
    expect(root(fixture).querySelector('.banner-line')?.textContent?.trim()).toContain('(thừa 95)');
  });

  it('shows ratio rows as one merged note without inputs or an allocate button', () => {
    const fixture = render();
    const row = dataRows(fixture)[2];

    expect(row.querySelectorAll('input').length).toBe(0);
    const merged = row.querySelector('.ratio-cell');
    expect(merged?.getAttribute('colspan')).toBe('4');
    expect(merged?.textContent?.trim()).toBe('Ngưỡng 0,5 áp dụng cho cả 4 quý, không chia');
    expect(row.querySelector('.total-cell')?.textContent?.trim()).toBe('—');
    expect(row.querySelector('.allocate-button')).toBeNull();
  });

  it('saves only non-ratio rows and emits saved', () => {
    const fixture = render();
    const saved = jasmine.createSpy('saved');
    fixture.componentInstance.saved.subscribe(saved);

    type(fixture, quarterInputs(fixture)[0], '30');
    buttonByText(fixture, 'Lưu nháp').click();
    fixture.detectChanges();

    expect(api.saveQuarterSplit).toHaveBeenCalledTimes(1);
    const [branchYearId, request] = api.saveQuarterSplit.calls.mostRecent().args;
    expect(branchYearId).toBe('by-1');
    expect(request.rows.map((row) => row.kpiId)).toEqual(['k1', 'k2']);
    expect(request.rows[0]).toEqual({ kpiId: 'k1', q1: 30, q2: 25, q3: 25, q4: 25 });
    expect(saved).toHaveBeenCalledTimes(1);
    expect(root(fixture).querySelector('.quarter-toast')?.textContent).toContain(
      'Đã lưu bản nháp.',
    );
  });

  it('keeps unsaved edits and shows the API error when saving fails', () => {
    const fixture = render({}, { save: 'locked' });
    const saved = jasmine.createSpy('saved');
    fixture.componentInstance.saved.subscribe(saved);

    const input = quarterInputs(fixture)[0];
    type(fixture, input, '40');
    buttonByText(fixture, 'Lưu nháp').click();
    fixture.detectChanges();

    expect(root(fixture).querySelector('.quarter-save-error')?.textContent?.trim()).toBe(
      'Quý này đã khóa hoặc đã kết thúc, không sửa được.',
    );
    expect(quarterInputs(fixture)[0].value).toBe('40');
    expect(saved).not.toHaveBeenCalled();
  });

  it('only enables Phân bổ when the row is matched and has no unsaved edits', () => {
    const fixture = render();

    const allocateButtons = () =>
      Array.from(root(fixture).querySelectorAll<HTMLButtonElement>('.allocate-button'));
    expect(allocateButtons().length).toBe(2);
    expect(allocateButtons().every((button) => !button.disabled)).toBeTrue();

    // Still matched, but the values differ from the saved state.
    const inputs = quarterInputs(fixture);
    type(fixture, inputs[0], '30');
    type(fixture, inputs[1], '20');
    expect(dataRows(fixture)[0].querySelector('.total-cell')?.textContent?.trim()).toBe('100 ✓');
    expect(allocateButtons()[0].disabled).toBeTrue();
    expect(allocateButtons()[1].disabled).toBeFalse();

    // A mismatch also blocks it.
    type(fixture, inputs[0], '10');
    expect(allocateButtons()[0].disabled).toBeTrue();

    buttonByText(fixture, 'Chia đều 4 quý').click();
    fixture.detectChanges();
    expect(allocateButtons()[0].disabled).toBeFalse();
  });

  it('renders a published split read-only', () => {
    const fixture = render({ status: 'ACTIVE' });

    expect(root(fixture).querySelector('.quarter-status')?.textContent?.trim()).toBe(
      'Đã phát hành',
    );
    expect(quarterInputs(fixture).length).toBe(0);
    expect(root(fixture).querySelectorAll('.quarter-static').length).toBe(8);
    expect(root(fixture).querySelector('.quarter-actions')).toBeNull();
    expect(root(fixture).querySelector('.allocate-button')).toBeNull();
  });

  it('shows the load error state with a retry button', () => {
    const fixture = render({}, { load: 'error' });

    expect(root(fixture).querySelector('.state-error')?.textContent).toContain(
      'Không tải được chỉ tiêu quý.',
    );
    expect(root(fixture).querySelector('.quarter-card')).toBeNull();
  });
});
