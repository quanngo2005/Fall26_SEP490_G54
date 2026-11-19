import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { I18nService } from '../../../core/i18n/i18n.service';
import { QuarterSplit, QuarterSplitRow } from '../../../core/target-assignment.models';
import { TargetAssignmentService } from '../../../core/target-assignment.service';
import { SuggestTargetsResponseDto, TargetSetupApiService } from '../target-setup-api.service';
import { BranchQuarterSplitComponent } from './branch-quarter-split.component';

const YEAR = 2026;

// Default unit is 'KH' (a plain count), not 'VND': these generic fixtures exercise the split math/UI at
// fine (whole-unit) precision. VND-specific tests below use their own realistic-magnitude row and assert
// the tỷ/triệu display and rounding separately.
function kpiRow(over: Partial<QuarterSplitRow> = {}): QuarterSplitRow {
  return {
    kpiId: 'k1',
    kpiCode: 'KPI_1',
    kpiName: 'KPI 1',
    unit: 'KH',
    groupCode: 'G',
    groupName: 'Group',
    aggregationType: 'SUM',
    isRatio: false,
    yearValue: 100,
    q1: 25,
    q2: 25,
    q3: 25,
    q4: 25,
    previousYearValue: null,
    previousYear: null,
    previousYearActual: null,
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

/** `kpis[].cells[0]` fixture for a `POST /target-suggestions/{year}` tier-1 response (contract section
 * "Suggestion engine"). */
function suggestionKpi(
  kpiId: string,
  kpiName: string,
  values: { year: number; q1: number; q2: number; q3: number; q4: number },
) {
  return {
    kpiId,
    kpiCode: kpiId.toUpperCase(),
    kpiName,
    unit: 'KH',
    aggregationType: 'SUM',
    cells: [
      {
        unitId: kpiId,
        unitName: kpiName,
        year: values.year,
        q1: values.q1,
        q2: values.q2,
        q3: values.q3,
        q4: values.q4,
        usedFallbackAverage: false,
        overwritten: true,
        locked: false,
        lockReason: null,
      },
    ],
    lockedUnits: [],
    total: values.year,
    sumMatches: true,
    disabledReason: null,
    warnings: [] as string[],
  };
}

function suggestionResponse(
  kpis: ReturnType<typeof suggestionKpi>[],
  basis: SuggestTargetsResponseDto['basis'] = 'TARGET_LAST_YEAR',
): SuggestTargetsResponseDto {
  return { tier: 1, basis, overwrite: 'EMPTY_ONLY', kpis, warnings: [] };
}

describe('BranchQuarterSplitComponent', () => {
  let api: jasmine.SpyObj<TargetAssignmentService>;
  let suggestApi: jasmine.SpyObj<TargetSetupApiService>;
  let state: QuarterSplit;

  function render(
    over: Partial<QuarterSplit> = {},
    options: {
      load?: 'ok' | 'error';
      save?: 'ok' | 'locked';
      suggest?: boolean;
      suggestResponse?: SuggestTargetsResponseDto;
    } = {},
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
    suggestApi = jasmine.createSpyObj<TargetSetupApiService>('TargetSetupApiService', ['suggest']);
    if (options.suggestResponse) {
      suggestApi.suggest.and.returnValue(of(options.suggestResponse));
    }

    TestBed.configureTestingModule({
      providers: [
        { provide: TargetAssignmentService, useValue: api },
        { provide: TargetSetupApiService, useValue: suggestApi },
      ],
    });
    TestBed.inject(I18nService).setLanguage('vi');

    const fixture = TestBed.createComponent(BranchQuarterSplitComponent);
    fixture.componentRef.setInput('branchYearId', 'by-1');
    fixture.componentRef.setInput('year', YEAR);
    if (options.suggest !== undefined) {
      fixture.componentRef.setInput('suggest', options.suggest);
    }
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

  /** Editable quarter cells render as `app-allocation-cell`'s own `<input class="cell-input">`; a LOCKED cell
   * renders a `<span>` instead (see `allocation-cell.component.html`), so this only picks up unlocked ones. */
  function quarterInputs(
    fixture: ComponentFixture<BranchQuarterSplitComponent>,
  ): HTMLInputElement[] {
    return Array.from(root(fixture).querySelectorAll<HTMLInputElement>('.cell-input'));
  }

  /** `app-allocation-cell` commits on blur (or Enter), not on every keystroke. */
  function type(
    fixture: ComponentFixture<BranchQuarterSplitComponent>,
    input: HTMLInputElement,
    value: string,
  ): void {
    input.value = value;
    input.dispatchEvent(new Event('blur'));
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
    expect(rows[0].querySelector('.unit-cell')?.textContent?.trim()).toBe('KH');
    expect(rows[0].querySelector('.year-cell')?.textContent).toContain('100');
    expect(rows[0].querySelector('.year-lock')).not.toBeNull();
    expect(rows[0].querySelector('.total-cell')?.textContent?.trim()).toBe('100 ✓');
  });

  it('has no equal-split button (D5: no equal split)', () => {
    const fixture = render();

    expect(
      Array.from(root(fixture).querySelectorAll('button')).some((b) =>
        b.textContent?.includes('Chia đều'),
      ),
    ).toBeFalse();
  });

  it('marks the row total red, the edited input red and lists the mismatch', () => {
    const fixture = render();

    type(fixture, quarterInputs(fixture)[0], '10');

    const row = dataRows(fixture)[0];
    expect(row.querySelector('.total-cell')?.classList).toContain('is-mismatch');
    expect(row.querySelector('.total-cell')?.textContent?.trim()).toBe('85 · thiếu 15');
    // The edited-but-mismatched cell renders as `app-allocation-cell`'s MISMATCH state.
    expect(row.querySelector('.quarter-cell .cell.is-mismatch')).not.toBeNull();

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

    // Typing the saved values back re-enables it.
    type(fixture, inputs[0], '25');
    type(fixture, inputs[1], '25');
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

  it('removed the head-office file-ratio button', () => {
    const fixture = render();

    expect(
      Array.from(root(fixture).querySelectorAll('button')).some((b) =>
        b.textContent?.includes('tỷ lệ file'),
      ),
    ).toBeFalse();
  });

  it('"✦ Gợi ý" opens the popover; "Tạo gợi ý" calls POST /target-suggestions/{year} (tier 1) and fills the draft', () => {
    const fixture = render();
    suggestApi.suggest.and.returnValue(
      of(
        suggestionResponse([
          suggestionKpi('k1', 'KPI 1', { year: 100, q1: 25, q2: 25, q3: 25, q4: 25 }),
          suggestionKpi('k2', 'KPI 2', { year: 10, q1: 1, q2: 2, q3: 3, q4: 4 }),
        ]),
      ),
    );

    expect(root(fixture).querySelector('.suggest-popover')).toBeNull();
    buttonByText(fixture, 'Gợi ý').click();
    fixture.detectChanges();
    expect(root(fixture).querySelector('.suggest-popover')).not.toBeNull();

    buttonByText(fixture, 'Tạo gợi ý').click();
    fixture.detectChanges();

    expect(suggestApi.suggest).toHaveBeenCalledTimes(1);
    const [year, request] = suggestApi.suggest.calls.mostRecent().args;
    expect(year).toBe(YEAR);
    expect(request.tier).toBe(1);
    expect(request.anchorId).toBe('by-1');
    expect(request.basis).toBe('TARGET_LAST_YEAR');

    const inputs = quarterInputs(fixture);
    expect(inputs.slice(0, 4).map((input) => input.value)).toEqual(['25', '25', '25', '25']);
    expect(inputs.slice(4, 8).map((input) => input.value)).toEqual(['1', '2', '3', '4']);

    const banner = root(fixture).querySelector('.preview-banner');
    expect(banner?.textContent).toContain('Đã gợi ý');
    expect(root(fixture).querySelector('.suggest-popover')).toBeNull();
  });

  it('"Hoàn tác" on the preview banner reverts the suggested values', () => {
    const fixture = render();
    suggestApi.suggest.and.returnValue(
      of(
        suggestionResponse([
          suggestionKpi('k1', 'KPI 1', { year: 100, q1: 40, q2: 20, q3: 20, q4: 20 }),
        ]),
      ),
    );

    buttonByText(fixture, 'Gợi ý').click();
    fixture.detectChanges();
    buttonByText(fixture, 'Tạo gợi ý').click();
    fixture.detectChanges();
    expect(quarterInputs(fixture)[0].value).toBe('40');

    buttonByText(fixture, 'Hoàn tác').click();
    fixture.detectChanges();

    expect(quarterInputs(fixture)[0].value).toBe('25');
    expect(root(fixture).querySelector('.preview-banner')).toBeNull();
  });

  it('"Áp dụng" on the preview banner keeps the suggested values and dismisses the banner', () => {
    const fixture = render();
    suggestApi.suggest.and.returnValue(
      of(
        suggestionResponse([
          suggestionKpi('k1', 'KPI 1', { year: 100, q1: 40, q2: 20, q3: 20, q4: 20 }),
        ]),
      ),
    );

    buttonByText(fixture, 'Gợi ý').click();
    fixture.detectChanges();
    buttonByText(fixture, 'Tạo gợi ý').click();
    fixture.detectChanges();

    buttonByText(fixture, 'Áp dụng').click();
    fixture.detectChanges();

    expect(root(fixture).querySelector('.preview-banner')).toBeNull();
    expect(quarterInputs(fixture)[0].value).toBe('40');
  });

  it('auto-runs the engine once when `suggest` is true and the year is DRAFT, unsaved', () => {
    const fixture = render(
      {},
      {
        suggest: true,
        suggestResponse: suggestionResponse([
          suggestionKpi('k1', 'KPI 1', { year: 100, q1: 10, q2: 20, q3: 30, q4: 40 }),
        ]),
      },
    );

    expect(suggestApi.suggest).toHaveBeenCalledTimes(1);
    expect(suggestApi.suggest.calls.mostRecent().args[1]).toEqual(
      jasmine.objectContaining({ tier: 1, basis: 'TARGET_LAST_YEAR', overwrite: 'UNLOCKED' }),
    );
    expect(
      quarterInputs(fixture)
        .slice(0, 4)
        .map((input) => input.value),
    ).toEqual(['10', '20', '30', '40']);
    expect(root(fixture).querySelector('.preview-banner')).not.toBeNull();
    expect(api.saveQuarterSplit).not.toHaveBeenCalled();
  });

  it('names the no-history fallback and the server warnings under the preview banner', () => {
    const kpi = suggestionKpi('k1', 'KPI 1', { year: 100, q1: 10, q2: 20, q3: 30, q4: 40 });
    const fallback = {
      ...kpi,
      cells: [{ ...kpi.cells[0], usedFallbackAverage: true }],
      warnings: ['Cảnh báo từ máy chủ'],
    };
    const fixture = render({}, { suggest: true, suggestResponse: suggestionResponse([fallback]) });

    const text = root(fixture).textContent ?? '';
    expect(text).toContain('1 chỉ tiêu chưa có lịch sử năm trước: chia tăng tiến Q1→Q4');
    expect(text).toContain('Cảnh báo từ máy chủ');
  });

  it('does not run the engine on open when `suggest` is false (default)', () => {
    render({}, { suggest: false });

    expect(suggestApi.suggest).not.toHaveBeenCalled();
  });

  it('does not run the engine on a read-only (published) split even with `suggest` true', () => {
    render({ status: 'ACTIVE' }, { suggest: true });

    expect(suggestApi.suggest).not.toHaveBeenCalled();
  });

  it('shows VND year/total columns in tỷ and a muted tỷ caption under each quarter input', () => {
    const fixture = render({
      rows: [
        kpiRow({
          unit: 'VND',
          yearValue: 120_000_000_000,
          q1: 30_000_000_000,
          q2: 30_000_000_000,
          q3: 30_000_000_000,
          q4: 30_000_000_000,
          sum: 120_000_000_000,
        }),
        kpiRow({ kpiId: 'k2', kpiName: 'KPI 2' }),
        ratioRow,
      ],
    });

    const row = dataRows(fixture)[0];
    expect(row.querySelector('.year-value')?.textContent?.trim()).toBe('120 tỷ VND');
    expect(row.querySelector('.total-cell')?.textContent?.trim()).toBe('120 tỷ VND ✓');
    const captions = Array.from(row.querySelectorAll('.quarter-caption')).map((el) =>
      el.textContent?.trim(),
    );
    expect(captions).toEqual(['30 tỷ VND', '30 tỷ VND', '30 tỷ VND', '30 tỷ VND']);
    // `app-allocation-cell`'s input carries the full, unformatted VND value (unaffected by the display
    // abbreviation above it).
    expect(quarterInputs(fixture)[0].value).toBe('30000000000');
  });

  it('does not add a tỷ/triệu caption for a non-VND unit', () => {
    const fixture = render();

    expect(root(fixture).querySelector('.quarter-caption')).toBeNull();
  });
});
