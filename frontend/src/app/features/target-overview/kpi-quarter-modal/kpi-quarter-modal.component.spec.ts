import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { EvaluationPeriodOption } from '../../../core/evaluation.models';
import { I18nService } from '../../../core/i18n/i18n.service';
import { KpiDefinition } from '../../../core/kpi-definition.models';
import { QuarterSplit, QuarterSplitRow } from '../../../core/target-assignment.models';
import { TargetAssignmentService } from '../../../core/target-assignment.service';
import { TargetSetupApiService } from '../../target-setup/target-setup-api.service';
import { KpiQuarterModalComponent } from './kpi-quarter-modal.component';

const kpi = {
  id: 'k1',
  code: 'KPI_TERM_DEP',
  name: 'Tiền gửi kỳ hạn mới',
  unit: 'VND',
  aggregationType: 'SUM',
  kpiGroupName: 'Huy động vốn',
} as KpiDefinition;

const row = (over: Partial<QuarterSplitRow> = {}): QuarterSplitRow => ({
  kpiId: 'k1',
  kpiCode: 'KPI_TERM_DEP',
  kpiName: 'Tiền gửi kỳ hạn mới',
  unit: 'VND',
  groupCode: 'SALES',
  groupName: 'Huy động vốn',
  aggregationType: 'SUM',
  isRatio: false,
  yearValue: 100_000_000_000,
  q1: 20_000_000_000,
  q2: 25_000_000_000,
  q3: 25_000_000_000,
  q4: 30_000_000_000,
  previousYearValue: 80_000_000_000,
  previousYear: {
    q1: 10_000_000_000,
    q2: 20_000_000_000,
    q3: 20_000_000_000,
    q4: 30_000_000_000,
  },
  previousYearActual: null,
  sum: 100_000_000_000,
  diff: 0,
  matched: true,
  ...over,
});

const split = (rows: QuarterSplitRow[], status = 'DRAFT'): QuarterSplit => ({
  branchYearId: 'by-1',
  year: 2027,
  status,
  importedAt: null,
  fileRatios: null,
  quarters: [1, 2, 3, 4].map((quarter) => ({ periodId: `p${quarter}`, quarter, locked: false })),
  rows,
});

const periods = [1, 4, 7, 10].map(
  (month, i) =>
    ({
      id: `q${i + 1}`,
      periodType: 'QUARTER',
      startDate: `2027-${String(month).padStart(2, '0')}-01`,
      endDate: `2027-${String(month + 2).padStart(2, '0')}-28`,
    }) as EvaluationPeriodOption,
);

describe('KpiQuarterModalComponent', () => {
  const targetApi = {
    getSetup: jasmine.createSpy('getSetup'),
    getQuarterSplit: jasmine.createSpy('getQuarterSplit'),
    saveQuarterSplit: jasmine.createSpy('saveQuarterSplit'),
  };
  const suggestionApi = { suggest: jasmine.createSpy('suggest') };
  const suggestion = (q: [number, number, number, number]) => ({
    tier: 1,
    basis: 'TARGET_LAST_YEAR',
    overwrite: 'UNLOCKED',
    warnings: [],
    kpis: [
      {
        kpiId: 'k1',
        warnings: [],
        cells: [
          {
            unitId: 'k1',
            q1: q[0],
            q2: q[1],
            q3: q[2],
            q4: q[3],
            year: 1,
            usedFallbackAverage: false,
          },
        ],
      },
    ],
  });

  beforeEach(() => {
    targetApi.getSetup.and.returnValue(of({ branchYearId: 'by-1' }));
    targetApi.getQuarterSplit.and.returnValue(of(split([row()])));
    targetApi.saveQuarterSplit.calls.reset();
    targetApi.saveQuarterSplit.and.returnValue(of(split([row()])));
    suggestionApi.suggest.calls.reset();
    suggestionApi.suggest.and.returnValue(of(suggestion([13e9, 25e9, 25e9, 37e9])));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: TargetAssignmentService, useValue: targetApi },
        { provide: TargetSetupApiService, useValue: suggestionApi },
      ],
    });
    TestBed.inject(I18nService).setLanguage('vi');
  });

  const render = (canManage = true): ComponentFixture<KpiQuarterModalComponent> => {
    const fixture = TestBed.createComponent(KpiQuarterModalComponent);
    fixture.componentRef.setInput('branchId', 'b');
    fixture.componentRef.setInput('branchName', 'Chi nhánh Hà Nội');
    fixture.componentRef.setInput('year', 2027);
    fixture.componentRef.setInput('kpi', kpi);
    fixture.componentRef.setInput('periods', periods);
    fixture.componentRef.setInput('canManage', canManage);
    fixture.detectChanges();
    return fixture;
  };

  it('shows the year target, the 4 quarters in tỷ VND and the change against last year', () => {
    const element = render().nativeElement as HTMLElement;

    expect(targetApi.getQuarterSplit).toHaveBeenCalledWith('by-1');
    expect(element.querySelector('h2')?.textContent).toContain('Tiền gửi kỳ hạn mới');
    expect(element.querySelector('.state-chip')?.textContent).toContain('Chia quý đủ');
    expect(element.querySelector('.tile strong')?.textContent).toContain('100');
    expect(element.querySelector('.meta')?.textContent).toContain('tỷ VND');
    const rows = element.querySelectorAll('.quarter-table tbody tr');
    expect(rows.length).toBe(5);
    expect(rows[0].textContent).toContain('20');
    expect(rows[0].textContent).toContain('▲ 100%');
    expect(element.querySelectorAll('.share-segment').length).toBe(4);
  });

  const clickSuggestion = (element: HTMLElement, label: string): void => {
    const buttons = [...element.querySelectorAll<HTMLButtonElement>('.suggestion-actions button')];
    buttons.find((b) => b.textContent?.includes(label))!.click();
  };

  it('asks the engine by last year targets and saves the suggested quarters', () => {
    const element = render().nativeElement as HTMLElement;
    clickSuggestion(element, 'Theo chỉ tiêu năm trước');

    expect(suggestionApi.suggest).toHaveBeenCalledOnceWith(
      2027,
      jasmine.objectContaining({
        tier: 1,
        basis: 'TARGET_LAST_YEAR',
        anchorId: 'by-1',
        kpiIds: ['k1'],
        overwrite: 'UNLOCKED',
      }),
    );
    expect(targetApi.saveQuarterSplit).toHaveBeenCalledWith('by-1', {
      rows: [{ kpiId: 'k1', q1: 13e9, q2: 25e9, q3: 25e9, q4: 37e9 }],
    });
  });

  it('asks by last year actuals and sends the locked quarters as they are', () => {
    const lockedSplit = split([row()]);
    lockedSplit.quarters = lockedSplit.quarters.map((q) =>
      q.quarter === 1 ? { ...q, locked: true } : q,
    );
    targetApi.getQuarterSplit.and.returnValue(of(lockedSplit));

    clickSuggestion(render().nativeElement as HTMLElement, 'Theo thực tế năm trước');

    const request = suggestionApi.suggest.calls.mostRecent().args[1];
    expect(request.basis).toBe('ACTUAL_LAST_YEAR');
    expect(request.currentValues).toEqual([
      { unitId: 'k1', year: 100e9, q1: 20e9, q2: null, q3: null, q4: null, locked: false },
    ]);
  });

  it('shows an error and saves nothing when the engine fails', () => {
    suggestionApi.suggest.and.returnValue(throwError(() => new Error('boom')));
    const fixture = render();
    const element = fixture.nativeElement as HTMLElement;
    clickSuggestion(element, 'Theo chỉ tiêu năm trước');
    fixture.detectChanges();

    expect(targetApi.saveQuarterSplit).not.toHaveBeenCalled();
    expect(element.querySelector('.save-error')).not.toBeNull();
  });

  it('is read only when the year is published or the user cannot manage targets', () => {
    targetApi.getQuarterSplit.and.returnValue(of(split([row()], 'ACTIVE')));
    expect(render().nativeElement.querySelector('.suggestion')).toBeNull();
    targetApi.getQuarterSplit.and.returnValue(of(split([row()])));
    const element = render(false).nativeElement as HTMLElement;
    expect(element.querySelector('.suggestion')).toBeNull();
    expect(element.querySelector('.button-primary')).toBeNull();
  });

  it('says when the year has not been imported', () => {
    targetApi.getSetup.and.returnValue(of({ branchYearId: null }));
    const element = render().nativeElement as HTMLElement;
    expect(element.querySelector('.state-line')?.textContent).toContain('chưa nhập');
  });
});
