import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { EvaluationService } from '../../core/evaluation.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { RankingPolicy, SimulateRankingPolicyResult } from '../../core/system-parameter.models';
import { SystemParameterService } from '../../core/system-parameter.service';
import { RankingStudioComponent } from './ranking-studio.component';

describe('RankingStudioComponent', () => {
  const samplePolicy: RankingPolicy = {
    id: 'pol-1',
    code: 'DEFAULT_POLICY',
    name: 'Chính sách gợi ý xếp hạng chuẩn Chi nhánh',
    description: 'Mặc định',
    version: 2,
    evaluationPeriodId: null,
    hasPeriodOverride: false,
    isLockedSnapshot: false,
    updatedAt: '2026-11-15T05:00:00Z',
    updatedBy: 'admin@bpms.com',
    definition: {
      scoreFormula: 'TotalScore',
      tiers: [
        { tierCode: 'A1', rankOrder: 1, minScore: 95 },
        { tierCode: 'A2', rankOrder: 2, minScore: 90 },
        { tierCode: 'A3', rankOrder: 3, minScore: 85 },
        { tierCode: 'B', rankOrder: 4, minScore: 70 },
        { tierCode: 'C', rankOrder: 5, minScore: 0 },
      ],
      tieBreakers: [
        'EFFECTIVE_SCORE_DESC',
        'BUSINESS_SCORE_DESC',
        'SERVICE_QUALITY_SCORE_DESC',
        'EMPLOYEE_CODE_ASC',
      ],
      rules: [
        {
          id: 'rule-1',
          name: 'Hạ trần rủi ro CRITICAL',
          enabled: true,
          stopOnMatch: false,
          conditions: {
            logicalOperator: 'AND',
            items: [{ metric: 'MAX_RISK_SEVERITY', operator: 'GTE', value: 4 }],
            groups: [],
          },
          actions: [{ type: 'CAP_MAX_TIER', tierCode: 'B', message: 'Hạ trần B' }],
        },
      ],
    },
    catalog: {
      formulaVariables: ['TotalScore', 'BusinessScore', 'DepartmentAvgScore', 'QualitativeScore'],
      formulaFunctions: ['IF', 'MIN', 'MAX', 'ROUND', 'CLAMP'],
      metrics: [
        'TOTAL_SCORE',
        'MIN_COMPONENT_SCORE',
        'MAX_RISK_SEVERITY',
        'CURRENT_TIER',
        'JOB_POSITION_CODE',
      ],
      operators: ['GTE', 'GT', 'LTE', 'LT', 'EQ', 'NEQ', 'BETWEEN', 'IN'],
      actionTypes: [
        'CAP_MAX_TIER',
        'SET_MIN_TIER',
        'SET_EXACT_TIER',
        'DEMOTE_STEPS',
        'PROMOTE_STEPS',
        'ENFORCE_GROUP_QUOTA_PERCENT',
        'SET_COACHING_FLAG',
        'SET_WARNING_LABEL',
      ],
      tieBreakerFields: [
        'EFFECTIVE_SCORE_DESC',
        'BUSINESS_SCORE_DESC',
        'SERVICE_QUALITY_SCORE_DESC',
        'EMPLOYEE_CODE_ASC',
      ],
    },
  };

  const simResult: SimulateRankingPolicyResult = {
    periodId: null,
    periodLabel: 'Bộ dữ liệu mô phỏng chuẩn Chi nhánh',
    isSyntheticSample: true,
    totalEmployees: 8,
    changedCount: 2,
    tierSummary: [
      { tierCode: 'A1', beforeCount: 1, beforePercent: 12.5, afterCount: 1, afterPercent: 12.5 },
      { tierCode: 'B', beforeCount: 3, beforePercent: 37.5, afterCount: 5, afterPercent: 62.5 },
    ],
    rows: [
      {
        employeeId: 'e1',
        employeeCode: 'NV-SIM-02',
        fullName: 'Trần Quốc Bảo',
        jobPositionCode: 'SALES_OFFICER',
        jobPositionTitle: 'CV QHKH',
        departmentName: 'Phòng KHCN',
        totalScore: 93.8,
        effectiveScore: 93.8,
        businessScore: 96,
        serviceQualityScore: 91,
        operationsScore: 92,
        qualitativeScore: 90,
        openRiskEvents: 1,
        maxRiskSeverity: 'CRITICAL',
        beforeRank: 2,
        afterRank: 2,
        beforeTier: 'A2',
        afterTier: 'B',
        changed: true,
        coachingFlag: true,
        warningLabel: null,
        matchedRules: ['Hạ trần rủi ro CRITICAL'],
        explanation: ['Điểm xét hạng 93.8 đạt A2', 'Rule [Hạ trần rủi ro CRITICAL]: Hạ trần B'],
      },
    ],
  };

  let getRankingPolicy: jasmine.Spy;
  let saveRankingPolicy: jasmine.Spy;
  let simulateRankingPolicy: jasmine.Spy;

  beforeEach(() => {
    localStorage.clear();
    getRankingPolicy = jasmine.createSpy('getRankingPolicy').and.returnValue(of(samplePolicy));
    saveRankingPolicy = jasmine
      .createSpy('saveRankingPolicy')
      .and.returnValue(of({ ...samplePolicy, version: 3 }));
    simulateRankingPolicy = jasmine
      .createSpy('simulateRankingPolicy')
      .and.returnValue(of(simResult));

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser: signal({ id: 'a', fullName: 'Admin', roles: ['ADMIN'], permissions: [] }),
          },
        },
        {
          provide: EvaluationService,
          useValue: {
            periods: () =>
              of([
                {
                  id: 'per-q3',
                  code: 'Q3-2026',
                  name: 'Quý 3/2026',
                  startDate: '2026-07-01',
                  endDate: '2026-09-30',
                  periodType: 'QUARTER',
                  status: 'OPEN',
                  isActive: true,
                },
              ]),
          },
        },
        {
          provide: SystemParameterService,
          useValue: { getRankingPolicy, saveRankingPolicy, simulateRankingPolicy },
        },
      ],
    });
  });

  afterEach(() => localStorage.clear());

  it('GB910_FE11 renders formula editor, base tiers, tie-breakers and visual WHEN-IF-THEN automation blocks', () => {
    const fixture = TestBed.createComponent(RankingStudioComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;

    expect(getRankingPolicy).toHaveBeenCalledWith(null);
    expect(root.querySelectorAll('.rule-block').length).toBe(1);
    expect(root.querySelectorAll('.tie-item').length).toBe(4);
    expect((root.querySelector('#score-formula-input') as HTMLTextAreaElement).value).toBe(
      'TotalScore',
    );
    fixture.destroy();
  });

  it('GB910_FE12 adds visual rule presets, reorders tie-breakers, runs Dry-Run Sandbox and saves policy', () => {
    const fixture = TestBed.createComponent(RankingStudioComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.applyFormulaPreset('unitPenalty');
    expect(component.scoreFormula()).toContain('IF(DepartmentAvgScore < 70');

    component.addRulePreset('quota');
    component.addRulePreset('veto');
    expect(component.rules().length).toBe(3);

    component.moveTieBreaker(2, -1);
    expect(component.tieBreakers()[1]).toBe('SERVICE_QUALITY_SCORE_DESC');

    component.runSimulation();
    fixture.detectChanges();
    expect(simulateRankingPolicy).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('Trần Quốc Bảo');

    component.savePolicy();
    fixture.detectChanges();
    expect(saveRankingPolicy).toHaveBeenCalledTimes(1);
    expect(component.savedVersion()).toBe(3);
    fixture.destroy();
  });

  it('GB910_FE13 renders cleanly in both Vietnamese and English without untranslated keys', () => {
    const fixture = TestBed.createComponent(RankingStudioComponent);
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('en');
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('h1')?.textContent).toBe(i18n.localization.rankingStudio.title);
    expect(root.textContent).not.toContain('rankingStudio.');
    fixture.destroy();
  });
});
