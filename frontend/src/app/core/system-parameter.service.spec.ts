import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import {
  EvaluationComparisonSetting,
  RankingPolicy,
  SimulateRankingPolicyResult,
  SystemParameter,
} from './system-parameter.models';
import { SystemParameterService } from './system-parameter.service';

describe('SystemParameterService', () => {
  let http: HttpTestingController;
  let service: SystemParameterService;
  const url = `${environment.apiUrl}/system-parameters`;
  const parameter: SystemParameter = {
    key: 'importMaxFileMb',
    value: 8,
    min: 1,
    max: 50,
    locked: false,
    updatedAt: null,
    updatedBy: null,
    valueType: 'INTEGER',
    group: 'IMPORT_HANDOVER',
  };
  const envelope = <T>(data: T) => ({ success: true, status: 200, data });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(SystemParameterService);
  });
  afterEach(() => http.verify());

  it('GB910_FE01 loads the parameter list with GET /system-parameters', () => {
    let result: SystemParameter[] = [];
    service.list().subscribe((items) => (result = items));
    const request = http.expectOne(url);
    expect(request.request.method).toBe('GET');
    request.flush(envelope([parameter]));
    expect(result).toEqual([parameter]);
  });

  it('GB910_FE02 sends typed changes with PUT /system-parameters and returns updated list', () => {
    let result: SystemParameter[] = [];
    const payload = {
      changes: [
        { key: 'importMaxFileMb' as const, value: 10 },
        { key: 'kpiLockTime' as const, value: '22:00:00' },
        { key: 'workOverdueEnabled' as const, value: false },
      ],
    };
    service.save(payload).subscribe((items) => (result = items));
    const request = http.expectOne(url);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(payload);
    request.flush(envelope([{ ...parameter, value: 10 }]));
    expect(result[0].value).toBe(10);
  });

  it('GB910_FE03 loads and saves evaluation comparison baseline', () => {
    let loaded: EvaluationComparisonSetting | undefined;
    service.getComparison().subscribe((res) => (loaded = res));
    const getReq = http.expectOne(`${url}/evaluation-comparison`);
    expect(getReq.request.method).toBe('GET');
    getReq.flush(envelope({ baseline: 'AVERAGE_LAST_N', lookbackPeriods: 4 }));
    expect(loaded).toEqual({ baseline: 'AVERAGE_LAST_N', lookbackPeriods: 4 });

    let saved: EvaluationComparisonSetting | undefined;
    service
      .saveComparison({ baseline: 'SAME_PERIOD_LAST_YEAR', lookbackPeriods: 4 })
      .subscribe((res) => (saved = res));
    const putReq = http.expectOne(`${url}/evaluation-comparison`);
    expect(putReq.request.method).toBe('PUT');
    putReq.flush(envelope({ baseline: 'SAME_PERIOD_LAST_YEAR', lookbackPeriods: 4 }));
    expect(saved?.baseline).toBe('SAME_PERIOD_LAST_YEAR');
  });

  it('GB910_FE04 loads, saves and simulates Ranking Policy via /system-parameters/ranking-policy', () => {
    const samplePolicy: RankingPolicy = {
      id: 'p1',
      code: 'DEFAULT_POLICY',
      name: 'Default',
      description: null,
      version: 1,
      evaluationPeriodId: null,
      hasPeriodOverride: false,
      isLockedSnapshot: false,
      updatedAt: null,
      updatedBy: null,
      definition: {
        scoreFormula: 'TotalScore',
        tiers: [
          { tierCode: 'A1', rankOrder: 1, minScore: 95 },
          { tierCode: 'B', rankOrder: 2, minScore: 0 },
        ],
        tieBreakers: ['EFFECTIVE_SCORE_DESC'],
        rules: [],
      },
      catalog: {
        formulaVariables: ['TotalScore'],
        formulaFunctions: ['IF'],
        metrics: ['TOTAL_SCORE'],
        operators: ['GTE'],
        actionTypes: ['CAP_MAX_TIER'],
        tieBreakerFields: ['EFFECTIVE_SCORE_DESC'],
      },
    };

    let policyResult: RankingPolicy | undefined;
    service.getRankingPolicy('period-1').subscribe((res) => (policyResult = res));
    const getReq = http.expectOne(`${url}/ranking-policy?periodId=period-1`);
    expect(getReq.request.method).toBe('GET');
    getReq.flush(envelope(samplePolicy));
    expect(policyResult).toEqual(samplePolicy);

    let simResult: SimulateRankingPolicyResult | undefined;
    service
      .simulateRankingPolicy({
        periodId: null,
        departmentId: null,
        definition: samplePolicy.definition,
      })
      .subscribe((res) => (simResult = res));
    const simReq = http.expectOne(`${url}/ranking-policy/simulate`);
    expect(simReq.request.method).toBe('POST');
    simReq.flush(
      envelope({
        periodId: null,
        periodLabel: 'Mock',
        isSyntheticSample: true,
        totalEmployees: 8,
        changedCount: 2,
        tierSummary: [],
        rows: [],
      }),
    );
    expect(simResult?.totalEmployees).toBe(8);
  });

  it('GB910_FE05 surfaces 400 VALIDATION_ERROR and 403 FORBIDDEN without faking success', () => {
    let status = 0;
    service.save({ changes: [{ key: 'importMaxFileMb', value: 999 }] }).subscribe({
      error: (error: { status: number }) => (status = error.status),
    });
    http
      .expectOne(url)
      .flush(
        { success: false, status: 400, errorCode: 'VALIDATION_ERROR', data: null },
        { status: 400, statusText: 'Bad Request' },
      );
    expect(status).toBe(400);

    let failed = false;
    service.list().subscribe({ error: () => (failed = true) });
    http.expectOne(url).flush({}, { status: 403, statusText: 'Forbidden' });
    expect(failed).toBeTrue();
  });

  it('rejects an unsuccessful or empty envelope', () => {
    let failed = false;
    service.list().subscribe({ error: () => (failed = true) });
    http.expectOne(url).flush({ success: false, status: 200, data: null, message: 'x' });
    expect(failed).toBeTrue();
  });
});
