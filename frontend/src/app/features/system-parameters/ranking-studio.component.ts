import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { EvaluationPeriodOption } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  RankingAutomationRule,
  RankingPolicy,
  RankingPolicyDefinition,
  RankingRuleAction,
  RankingRuleActionType,
  RankingRuleConditionItem,
  RankingTierDefinition,
  SimulateRankingPolicyResult,
} from '../../core/system-parameter.models';
import { SystemParameterService } from '../../core/system-parameter.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { describeFormula, parseFormula } from './ranking-formula';
import { ifNote, thenNote, whenNote } from './ranking-rule-notes';

const STRING_METRICS = new Set(['CURRENT_TIER', 'JOB_POSITION_CODE']);

/** SCR-ADM-04 / GB-910: Visual Ranking Suggestion Automation Studio. */
@Component({
  selector: 'app-ranking-studio',
  imports: [RouterLink, HeaderComponent, SidebarComponent],
  templateUrl: './ranking-studio.component.html',
  styleUrls: [
    './admin-config-shell.scss',
    './ranking-studio.component.scss',
    './ranking-studio-formula.scss',
    './ranking-studio-pipeline.scss',
  ],
})
export class RankingStudioComponent extends LocalizedComponent {
  private readonly api = inject(SystemParameterService);
  private readonly evaluationApi = inject(EvaluationService);

  readonly auditEntityName = 'RankingPolicy';
  readonly periods = signal<EvaluationPeriodOption[]>([]);
  readonly selectedPeriodId = signal<string>('');

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly simulating = signal(false);
  readonly error = signal<'loadFailed' | 'saveFailed' | 'simulateFailed' | null>(null);
  readonly savedVersion = signal<number | null>(null);

  readonly policyMeta = signal<{
    version: number;
    hasPeriodOverride: boolean;
    isLockedSnapshot: boolean;
    updatedAt: string | null;
    updatedBy: string | null;
  }>({
    version: 1,
    hasPeriodOverride: false,
    isLockedSnapshot: false,
    updatedAt: null,
    updatedBy: null,
  });

  readonly policyName = signal('');
  readonly policyDescription = signal('');
  readonly scoreFormula = signal('TotalScore');
  readonly tiers = signal<RankingTierDefinition[]>([]);
  readonly tieBreakers = signal<string[]>([]);
  readonly rules = signal<RankingAutomationRule[]>([]);

  readonly catalog = signal({
    formulaVariables: [
      'TotalScore',
      'WeightedScore',
      'QuantitativeScore',
      'QualitativeScore',
      'BusinessScore',
      'ServiceQualityScore',
      'OperationsScore',
      'RiskPenalty',
      'DepartmentAvgScore',
      'CriticalRiskCount',
    ],
    formulaFunctions: ['IF', 'MIN', 'MAX', 'ROUND', 'ABS', 'CLAMP'],
    metrics: [
      'TOTAL_SCORE',
      'EFFECTIVE_SCORE',
      'QUANTITATIVE_SCORE',
      'QUALITATIVE_SCORE',
      'BUSINESS_SCORE',
      'SERVICE_QUALITY_SCORE',
      'OPERATIONS_SCORE',
      'MIN_COMPONENT_SCORE',
      'SCORE_CHANGE_VS_PREV',
      'OPEN_RISK_EVENTS',
      'MAX_RISK_SEVERITY',
      'CRITICAL_RISK_COUNT',
      'HIGH_OR_ABOVE_RISK_COUNT',
      'PERCENTILE_IN_GROUP',
      'RANK_IN_GROUP',
      'GROUP_SIZE',
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
    ] as RankingRuleActionType[],
    tieBreakerFields: [
      'EFFECTIVE_SCORE_DESC',
      'TOTAL_SCORE_DESC',
      'BUSINESS_SCORE_DESC',
      'SERVICE_QUALITY_SCORE_DESC',
      'OPERATIONS_SCORE_DESC',
      'QUALITATIVE_SCORE_DESC',
      'RISK_PENALTY_ASC',
      'EMPLOYEE_CODE_ASC',
    ],
  });

  readonly simulation = signal<SimulateRankingPolicyResult | null>(null);

  /** Parsed Effective Score formula, or null when it has a syntax error. */
  readonly formulaTree = computed(() =>
    parseFormula(this.scoreFormula(), this.catalog().formulaVariables),
  );

  /** One plain-language sentence that reads the whole formula aloud. */
  readonly formulaExplanation = computed(() => {
    const tree = this.formulaTree();
    return tree
      ? describeFormula(tree, this.localization.rankingStudio.formula, (name) =>
          this.formulaVariableLabel(name),
        )
      : '';
  });

  constructor() {
    super();
    this.loadInitial();
  }

  loadInitial(): void {
    this.loading.set(true);
    this.error.set(null);
    const periods$ =
      typeof this.evaluationApi.periods === 'function'
        ? this.evaluationApi.periods().pipe(catchError(() => of([])))
        : of([]);

    forkJoin({
      periods: periods$,
      policy: this.api.getRankingPolicy(null),
    }).subscribe({
      next: ({ periods, policy }) => {
        this.periods.set(periods);
        this.applyPolicy(policy);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('loadFailed');
        this.loading.set(false);
      },
    });
  }

  onScopeChange(event: Event): void {
    const periodId = (event.target as HTMLSelectElement).value;
    this.selectedPeriodId.set(periodId);
    this.savedVersion.set(null);
    this.simulation.set(null);
    this.loadPolicyForScope(periodId || null);
  }

  loadPolicyForScope(periodId: string | null): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getRankingPolicy(periodId).subscribe({
      next: (policy) => {
        this.applyPolicy(policy);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('loadFailed');
        this.loading.set(false);
      },
    });
  }

  insertToken(token: string, isFunction = false, editor?: HTMLTextAreaElement): void {
    if (this.policyMeta().isLockedSnapshot) return;
    const snippet = isFunction
      ? token === 'IF'
        ? 'IF(DepartmentAvgScore < 70, TotalScore * 0.8, TotalScore)'
        : `${token}(TotalScore, 100)`
      : token;
    const current = this.scoreFormula();
    if (editor && typeof editor.selectionStart === 'number') {
      const start = editor.selectionStart;
      const end = editor.selectionEnd ?? start;
      const next = `${current.slice(0, start)}${snippet}${current.slice(end)}`;
      this.scoreFormula.set(next);
      return;
    }
    this.scoreFormula.set(current.trim() ? `${current.trim()} + ${snippet}` : snippet);
  }

  formulaVariableLabel(name: string): string {
    const dict = this.localization.rankingStudio.formula.variables as Record<string, string>;
    return dict[name] ?? name;
  }

  ruleWhenNote(rule: RankingAutomationRule): string {
    return whenNote(rule, this.localization.rankingStudio.notes);
  }

  ruleIfNote(rule: RankingAutomationRule): string {
    return ifNote(rule, this.localization.rankingStudio.notes);
  }

  ruleThenNote(rule: RankingAutomationRule): string {
    return thenNote(rule, this.localization.rankingStudio.notes);
  }

  resetDefaultTemplate(): void {
    if (this.policyMeta().isLockedSnapshot) return;
    this.scoreFormula.set('TotalScore');
    this.tiers.set([
      { tierCode: 'A1', rankOrder: 1, minScore: 95 },
      { tierCode: 'A2', rankOrder: 2, minScore: 90 },
      { tierCode: 'A3', rankOrder: 3, minScore: 85 },
      { tierCode: 'B', rankOrder: 4, minScore: 70 },
      { tierCode: 'C', rankOrder: 5, minScore: 0 },
    ]);
    this.tieBreakers.set([
      'EFFECTIVE_SCORE_DESC',
      'BUSINESS_SCORE_DESC',
      'SERVICE_QUALITY_SCORE_DESC',
      'EMPLOYEE_CODE_ASC',
    ]);
    this.rules.set([
      {
        id: 'rule-critical-risk-cap',
        name: 'Hạ trần tối đa hạng B khi có sự kiện rủi ro nghiêm trọng',
        enabled: true,
        stopOnMatch: false,
        conditions: {
          logicalOperator: 'AND',
          items: [{ metric: 'MAX_RISK_SEVERITY', operator: 'GTE', value: 4 }],
          groups: [],
        },
        actions: [
          {
            type: 'CAP_MAX_TIER',
            tierCode: 'B',
            message: 'Hạ trần tối đa hạng B do phát sinh sự kiện rủi ro mức nghiêm trọng',
          },
        ],
      },
      {
        id: 'rule-low-tier-coaching',
        name: 'Tự động gắn cờ kèm cặp cho hạng C trở xuống',
        enabled: true,
        stopOnMatch: false,
        conditions: {
          logicalOperator: 'AND',
          items: [{ metric: 'CURRENT_TIER', operator: 'IN', values: ['C', 'D'] }],
          groups: [],
        },
        actions: [
          {
            type: 'SET_COACHING_FLAG',
            flagValue: true,
            message: 'Đề xuất kèm cặp do xếp hạng từ C trở xuống',
          },
        ],
      },
    ]);
  }

  applyFormulaPreset(kind: 'standard' | 'unitPenalty' | 'balanced'): void {
    if (this.policyMeta().isLockedSnapshot) return;
    if (kind === 'standard') {
      this.scoreFormula.set('TotalScore');
    } else if (kind === 'unitPenalty') {
      this.scoreFormula.set('IF(DepartmentAvgScore < 70, ROUND(TotalScore * 0.8, 2), TotalScore)');
    } else {
      this.scoreFormula.set('CLAMP(MIN(TotalScore, QualitativeScore + 12), 0, 100)');
    }
  }

  updateTierCode(index: number, event: Event): void {
    const code = (event.target as HTMLInputElement).value.trim().toUpperCase();
    this.tiers.update((list) =>
      list.map((item, idx) => (idx === index ? { ...item, tierCode: code } : item)),
    );
  }

  updateTierMinScore(index: number, event: Event): void {
    const num = Number((event.target as HTMLInputElement).value);
    this.tiers.update((list) =>
      list.map((item, idx) =>
        idx === index ? { ...item, minScore: Number.isFinite(num) ? num : 0 } : item,
      ),
    );
  }

  addTier(): void {
    if (this.tiers().length >= 10 || this.policyMeta().isLockedSnapshot) return;
    this.tiers.update((list) => {
      const nextCode = list.some((t) => t.tierCode === 'D') ? `T${list.length + 1}` : 'D';
      const updated = list.map((t, i) =>
        i === list.length - 1 && t.minScore === 0 ? { ...t, minScore: 50 } : t,
      );
      return [...updated, { tierCode: nextCode, rankOrder: list.length + 1, minScore: 0 }];
    });
  }

  removeTier(index: number): void {
    if (this.tiers().length <= 2 || this.policyMeta().isLockedSnapshot) return;
    this.tiers.update((list) =>
      list.filter((_, idx) => idx !== index).map((item, idx) => ({ ...item, rankOrder: idx + 1 })),
    );
  }

  moveTieBreaker(index: number, delta: -1 | 1): void {
    const target = index + delta;
    const current = [...this.tieBreakers()];
    if (target < 0 || target >= current.length || this.policyMeta().isLockedSnapshot) return;
    [current[index], current[target]] = [current[target], current[index]];
    this.tieBreakers.set(current);
  }

  toggleTieBreakerField(field: string, event: Event): void {
    if (this.policyMeta().isLockedSnapshot) return;
    const checked = (event.target as HTMLInputElement).checked;
    if (checked) {
      this.tieBreakers.update((list) => (list.includes(field) ? list : [...list, field]));
    } else if (this.tieBreakers().length > 1) {
      this.tieBreakers.update((list) => list.filter((item) => item !== field));
    }
  }

  addRulePreset(kind: 'blank' | 'risk' | 'veto' | 'quota'): void {
    if (this.policyMeta().isLockedSnapshot) return;
    const fallbackTier = this.tiers()[Math.min(3, this.tiers().length - 1)]?.tierCode ?? 'B';
    const id = `rule-${Date.now()}-${this.rules().length + 1}`;
    let rule: RankingAutomationRule;

    if (kind === 'risk') {
      rule = {
        id,
        name: 'Khống chế trần hạng khi có rủi ro nghiêm trọng',
        enabled: true,
        stopOnMatch: false,
        conditions: {
          logicalOperator: 'AND',
          items: [{ metric: 'MAX_RISK_SEVERITY', operator: 'GTE', value: 4 }],
          groups: [],
        },
        actions: [
          {
            type: 'CAP_MAX_TIER',
            tierCode: fallbackTier,
            message: `Hạ trần tối đa hạng ${fallbackTier} do phát sinh sự kiện rủi ro mức nghiêm trọng`,
          },
          { type: 'SET_COACHING_FLAG', flagValue: true },
        ],
      };
    } else if (kind === 'veto') {
      rule = {
        id,
        name: 'Điểm liệt nhóm tiêu chí < 50 điểm',
        enabled: true,
        stopOnMatch: false,
        conditions: {
          logicalOperator: 'AND',
          items: [{ metric: 'MIN_COMPONENT_SCORE', operator: 'LT', value: 50 }],
          groups: [],
        },
        actions: [
          {
            type: 'DEMOTE_STEPS',
            numericValue: 1,
            message: 'Hạ 1 bậc xếp hạng do có nhóm tiêu chí thành phần dưới 50 điểm',
          },
        ],
      };
    } else if (kind === 'quota') {
      rule = {
        id,
        name: 'Kiểm soát hạn ngạch top 20% nhóm hạng A',
        enabled: true,
        stopOnMatch: false,
        conditions: {
          logicalOperator: 'AND',
          items: [{ metric: 'CURRENT_TIER', operator: 'IN', values: ['A1', 'A2', 'A3'] }],
          groups: [],
        },
        actions: [
          {
            type: 'ENFORCE_GROUP_QUOTA_PERCENT',
            tierCode: fallbackTier,
            numericValue: 20,
            message: `Chuyển xuống ${fallbackTier} do vượt hạn ngạch top 20% hạng A của đơn vị`,
          },
        ],
      };
    } else {
      rule = {
        id,
        name: `Quy tắc tự động #${this.rules().length + 1}`,
        enabled: true,
        stopOnMatch: false,
        conditions: {
          logicalOperator: 'AND',
          items: [{ metric: 'TOTAL_SCORE', operator: 'LT', value: 70 }],
          groups: [],
        },
        actions: [{ type: 'SET_COACHING_FLAG', flagValue: true }],
      };
    }

    this.rules.update((list) => [...list, rule]);
  }

  moveRule(index: number, delta: -1 | 1): void {
    const target = index + delta;
    const list = [...this.rules()];
    if (target < 0 || target >= list.length || this.policyMeta().isLockedSnapshot) return;
    [list[index], list[target]] = [list[target], list[index]];
    this.rules.set(list);
  }

  deleteRule(index: number): void {
    if (this.policyMeta().isLockedSnapshot) return;
    this.rules.update((list) => list.filter((_, idx) => idx !== index));
  }

  updateRuleName(index: number, event: Event): void {
    const name = (event.target as HTMLInputElement).value;
    this.rules.update((list) => list.map((r, i) => (i === index ? { ...r, name } : r)));
  }

  toggleRuleEnabled(index: number, event: Event): void {
    const enabled = (event.target as HTMLInputElement).checked;
    this.rules.update((list) => list.map((r, i) => (i === index ? { ...r, enabled } : r)));
  }

  toggleRuleStop(index: number, event: Event): void {
    const stopOnMatch = (event.target as HTMLInputElement).checked;
    this.rules.update((list) => list.map((r, i) => (i === index ? { ...r, stopOnMatch } : r)));
  }

  updateLogicalOp(index: number, event: Event): void {
    const logicalOperator = (event.target as HTMLSelectElement).value as 'AND' | 'OR';
    this.rules.update((list) =>
      list.map((r, i) =>
        i === index ? { ...r, conditions: { ...r.conditions, logicalOperator } } : r,
      ),
    );
  }

  addCondition(ruleIdx: number): void {
    this.rules.update((list) =>
      list.map((r, i) =>
        i === ruleIdx
          ? {
              ...r,
              conditions: {
                ...r.conditions,
                items: [
                  ...r.conditions.items,
                  { metric: 'BUSINESS_SCORE', operator: 'GTE', value: 80 },
                ],
              },
            }
          : r,
      ),
    );
  }

  removeCondition(ruleIdx: number, condIdx: number): void {
    this.rules.update((list) =>
      list.map((r, i) =>
        i === ruleIdx && r.conditions.items.length > 1
          ? {
              ...r,
              conditions: {
                ...r.conditions,
                items: r.conditions.items.filter((_, ci) => ci !== condIdx),
              },
            }
          : r,
      ),
    );
  }

  isStringMetric(metric: string): boolean {
    return STRING_METRICS.has(metric);
  }

  operatorsForMetric(metric: string): string[] {
    return this.isStringMetric(metric)
      ? ['IN', 'EQ', 'NEQ']
      : ['GTE', 'GT', 'LTE', 'LT', 'EQ', 'NEQ', 'BETWEEN'];
  }

  updateConditionMetric(ruleIdx: number, condIdx: number, event: Event): void {
    const metric = (event.target as HTMLSelectElement).value;
    const isStr = this.isStringMetric(metric);
    this.rules.update((list) =>
      list.map((r, i) => {
        if (i !== ruleIdx) return r;
        const items = r.conditions.items.map((c, ci) => {
          if (ci !== condIdx) return c;
          return isStr
            ? {
                metric,
                operator: 'IN',
                values: metric === 'CURRENT_TIER' ? ['A1', 'A2', 'A3'] : ['SALES_OFFICER'],
                value: null,
                secondaryValue: null,
              }
            : { metric, operator: 'GTE', value: 80, secondaryValue: null, values: null };
        });
        return { ...r, conditions: { ...r.conditions, items } };
      }),
    );
  }

  updateConditionOperator(ruleIdx: number, condIdx: number, event: Event): void {
    const operator = (event.target as HTMLSelectElement).value;
    this.rules.update((list) =>
      list.map((r, i) => {
        if (i !== ruleIdx) return r;
        const items = r.conditions.items.map((c, ci) =>
          ci === condIdx
            ? {
                ...c,
                operator,
                secondaryValue: operator === 'BETWEEN' ? (c.secondaryValue ?? 100) : null,
              }
            : c,
        );
        return { ...r, conditions: { ...r.conditions, items } };
      }),
    );
  }

  updateConditionValue(ruleIdx: number, condIdx: number, secondary: boolean, event: Event): void {
    const num = Number((event.target as HTMLInputElement).value);
    const val = Number.isFinite(num) ? num : 0;
    this.rules.update((list) =>
      list.map((r, i) => {
        if (i !== ruleIdx) return r;
        const items = r.conditions.items.map((c, ci) =>
          ci === condIdx ? (secondary ? { ...c, secondaryValue: val } : { ...c, value: val }) : c,
        );
        return { ...r, conditions: { ...r.conditions, items } };
      }),
    );
  }

  formatConditionValues(cond: RankingRuleConditionItem): string {
    return (cond.values ?? []).join(', ');
  }

  updateConditionStringValues(ruleIdx: number, condIdx: number, event: Event): void {
    const raw = (event.target as HTMLInputElement).value;
    const values = raw
      .split(',')
      .map((s) => s.trim())
      .filter((s) => s.length > 0);
    this.rules.update((list) =>
      list.map((r, i) => {
        if (i !== ruleIdx) return r;
        const items = r.conditions.items.map((c, ci) => (ci === condIdx ? { ...c, values } : c));
        return { ...r, conditions: { ...r.conditions, items } };
      }),
    );
  }

  addAction(ruleIdx: number): void {
    const defaultTier = this.tiers()[Math.min(3, this.tiers().length - 1)]?.tierCode ?? 'B';
    this.rules.update((list) =>
      list.map((r, i) =>
        i === ruleIdx
          ? { ...r, actions: [...r.actions, { type: 'CAP_MAX_TIER', tierCode: defaultTier }] }
          : r,
      ),
    );
  }

  removeAction(ruleIdx: number, actIdx: number): void {
    this.rules.update((list) =>
      list.map((r, i) =>
        i === ruleIdx && r.actions.length > 1
          ? { ...r, actions: r.actions.filter((_, ai) => ai !== actIdx) }
          : r,
      ),
    );
  }

  updateActionType(ruleIdx: number, actIdx: number, event: Event): void {
    const type = (event.target as HTMLSelectElement).value as RankingRuleActionType;
    const defaultTier = this.tiers()[Math.min(3, this.tiers().length - 1)]?.tierCode ?? 'B';
    const nextAction: RankingRuleAction =
      type === 'CAP_MAX_TIER' || type === 'SET_MIN_TIER' || type === 'SET_EXACT_TIER'
        ? { type, tierCode: defaultTier, message: '' }
        : type === 'ENFORCE_GROUP_QUOTA_PERCENT'
          ? { type, tierCode: defaultTier, numericValue: 20, message: '' }
          : type === 'DEMOTE_STEPS' || type === 'PROMOTE_STEPS'
            ? { type, numericValue: 1, message: '' }
            : type === 'SET_COACHING_FLAG'
              ? { type, flagValue: true, message: '' }
              : { type, message: 'Cần lưu ý kiểm soát' };

    this.rules.update((list) =>
      list.map((r, i) =>
        i === ruleIdx
          ? { ...r, actions: r.actions.map((a, ai) => (ai === actIdx ? nextAction : a)) }
          : r,
      ),
    );
  }

  updateActionTier(ruleIdx: number, actIdx: number, event: Event): void {
    const tierCode = (event.target as HTMLSelectElement).value;
    this.rules.update((list) =>
      list.map((r, i) =>
        i === ruleIdx
          ? { ...r, actions: r.actions.map((a, ai) => (ai === actIdx ? { ...a, tierCode } : a)) }
          : r,
      ),
    );
  }

  updateActionNumber(ruleIdx: number, actIdx: number, event: Event): void {
    const num = Number((event.target as HTMLInputElement).value);
    this.rules.update((list) =>
      list.map((r, i) =>
        i === ruleIdx
          ? {
              ...r,
              actions: r.actions.map((a, ai) =>
                ai === actIdx ? { ...a, numericValue: Number.isFinite(num) ? num : 1 } : a,
              ),
            }
          : r,
      ),
    );
  }

  updateActionMessage(ruleIdx: number, actIdx: number, event: Event): void {
    const message = (event.target as HTMLInputElement).value;
    this.rules.update((list) =>
      list.map((r, i) =>
        i === ruleIdx
          ? { ...r, actions: r.actions.map((a, ai) => (ai === actIdx ? { ...a, message } : a)) }
          : r,
      ),
    );
  }

  needsActionTier(type: RankingRuleActionType): boolean {
    return (
      type === 'CAP_MAX_TIER' ||
      type === 'SET_MIN_TIER' ||
      type === 'SET_EXACT_TIER' ||
      type === 'ENFORCE_GROUP_QUOTA_PERCENT'
    );
  }

  needsActionNumber(type: RankingRuleActionType): boolean {
    return (
      type === 'DEMOTE_STEPS' || type === 'PROMOTE_STEPS' || type === 'ENFORCE_GROUP_QUOTA_PERCENT'
    );
  }

  runSimulation(): void {
    if (this.simulating()) return;
    this.simulating.set(true);
    this.error.set(null);
    this.api
      .simulateRankingPolicy({
        periodId: this.selectedPeriodId() || null,
        departmentId: null,
        definition: this.buildDefinition(),
      })
      .subscribe({
        next: (result) => {
          this.simulation.set(result);
          this.simulating.set(false);
        },
        error: () => {
          this.error.set('simulateFailed');
          this.simulating.set(false);
        },
      });
  }

  savePolicy(): void {
    if (this.saving() || this.policyMeta().isLockedSnapshot) return;
    this.saving.set(true);
    this.error.set(null);
    this.savedVersion.set(null);
    this.api
      .saveRankingPolicy({
        name: this.policyName().trim() || 'Chính sách gợi ý xếp hạng Chi nhánh',
        description: this.policyDescription().trim() || null,
        evaluationPeriodId: this.selectedPeriodId() || null,
        definition: this.buildDefinition(),
      })
      .subscribe({
        next: (saved) => {
          this.applyPolicy(saved);
          this.savedVersion.set(saved.version);
          this.saving.set(false);
        },
        error: () => {
          this.error.set('saveFailed');
          this.saving.set(false);
        },
      });
  }

  metricLabel(code: string): string {
    const dict = this.localization.rankingStudio.metrics as Record<string, string>;
    return dict[code] ?? code;
  }

  operatorLabel(code: string): string {
    const dict = this.localization.rankingStudio.operators as Record<string, string>;
    return dict[code] ?? code;
  }

  actionLabel(code: string): string {
    const dict = this.localization.rankingStudio.actions as Record<string, string>;
    return dict[code] ?? code;
  }

  tieBreakerLabel(code: string): string {
    const dict = this.localization.rankingStudio.tieBreakers as Record<string, string>;
    return dict[code] ?? code;
  }

  private buildDefinition(): RankingPolicyDefinition {
    return {
      scoreFormula: this.scoreFormula().trim() || 'TotalScore',
      tiers: this.tiers().map((t, idx) => ({
        tierCode: t.tierCode.trim() || `T${idx + 1}`,
        rankOrder: idx + 1,
        minScore: t.minScore,
      })),
      tieBreakers: this.tieBreakers(),
      rules: this.rules(),
    };
  }

  private applyPolicy(policy: RankingPolicy): void {
    this.policyMeta.set({
      version: policy.version,
      hasPeriodOverride: policy.hasPeriodOverride,
      isLockedSnapshot: policy.isLockedSnapshot,
      updatedAt: policy.updatedAt,
      updatedBy: policy.updatedBy,
    });
    this.policyName.set(policy.name);
    this.policyDescription.set(policy.description ?? '');
    this.scoreFormula.set(policy.definition.scoreFormula);
    this.tiers.set(policy.definition.tiers.map((t) => ({ ...t })));
    this.tieBreakers.set([...policy.definition.tieBreakers]);
    this.rules.set(
      policy.definition.rules.map((r) => ({
        ...r,
        conditions: {
          logicalOperator: r.conditions.logicalOperator,
          items: r.conditions.items.map((c) => ({
            ...c,
            values: c.values ? [...c.values] : null,
          })),
          groups: r.conditions.groups ?? [],
        },
        actions: r.actions.map((a) => ({ ...a })),
      })),
    );
    if (policy.catalog?.metrics?.length) {
      this.catalog.set({
        formulaVariables: policy.catalog.formulaVariables,
        formulaFunctions: policy.catalog.formulaFunctions,
        metrics: policy.catalog.metrics,
        operators: policy.catalog.operators,
        actionTypes: policy.catalog.actionTypes as RankingRuleActionType[],
        tieBreakerFields: policy.catalog.tieBreakerFields,
      });
    }
  }
}
