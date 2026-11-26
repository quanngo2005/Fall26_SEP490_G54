import type { Localization } from '../../core/i18n/i18n.models';
import type {
  RankingAutomationRule,
  RankingRuleAction,
  RankingRuleConditionItem,
} from '../../core/system-parameter.models';
import { joinList } from './ranking-formula';

/** Plain-language notes shown under the WHEN / IF / THEN blocks of an automation rule. */
export type RuleNoteWords = Localization['rankingStudio']['notes'];

const SCORE_METRICS = new Set([
  'TOTAL_SCORE',
  'EFFECTIVE_SCORE',
  'QUANTITATIVE_SCORE',
  'QUALITATIVE_SCORE',
  'BUSINESS_SCORE',
  'SERVICE_QUALITY_SCORE',
  'OPERATIONS_SCORE',
  'MIN_COMPONENT_SCORE',
  'SCORE_CHANGE_VS_PREV',
]);

const fill = (template: string, params: Record<string, string | number>): string =>
  template.replace(/\{(\w+)\}/g, (_, key: string) => String(params[key] ?? ''));

const lookup = (dict: object, key: string): string | undefined =>
  (dict as Record<string, string>)[key];

const formatNumber = (value: number | null | undefined, words: RuleNoteWords): string =>
  value === null || value === undefined || !Number.isFinite(value)
    ? words.emptyValue
    : String(value).replace('.', words.decimal);

const formatValue = (
  metric: string,
  value: number | null | undefined,
  words: RuleNoteWords,
): string => {
  const text = formatNumber(value, words);
  if (value === null || value === undefined) return text;
  if (SCORE_METRICS.has(metric)) return `${text} ${words.unitScore}`;
  if (metric === 'PERCENTILE_IN_GROUP') return `${text}${words.unitPercent}`;
  if (metric === 'MAX_RISK_SEVERITY') {
    const label = lookup(words.severity, String(value));
    return label ? fill(words.severityValue, { value: text, label }) : text;
  }
  return text;
};

const describeCondition = (cond: RankingRuleConditionItem, words: RuleNoteWords): string => {
  const metric = lookup(words.metrics, cond.metric) ?? cond.metric;
  const values = (cond.values ?? []).filter((v) => v.trim().length > 0);
  const listText = values.length ? joinList(values, words.listOr) : words.emptyValue;

  if (cond.operator === 'IN') return fill(words.inList, { metric, values: listText });
  if (cond.operator === 'BETWEEN') {
    return fill(words.between, {
      metric,
      min: formatValue(cond.metric, cond.value, words),
      max: formatValue(cond.metric, cond.secondaryValue, words),
    });
  }
  if (values.length && (cond.operator === 'EQ' || cond.operator === 'NEQ')) {
    return fill(cond.operator === 'EQ' ? words.isValue : words.notValue, {
      metric,
      value: listText,
    });
  }
  return fill(words.compare, {
    metric,
    op: lookup(words.ops, cond.operator) ?? cond.operator,
    value: formatValue(cond.metric, cond.value, words),
  });
};

const describeAction = (action: RankingRuleAction, words: RuleNoteWords): string => {
  const template = lookup(words.actions, action.type) ?? action.type;
  const message = action.message?.trim();
  return fill(template, {
    tier: action.tierCode || words.emptyValue,
    n: action.numericValue ?? 1,
    message: message ? `“${message}”` : '',
  }).trim();
};

export const whenNote = (rule: RankingAutomationRule, words: RuleNoteWords): string =>
  rule.enabled ? words.when : `${words.when} ${words.whenDisabled}`;

export const ifNote = (rule: RankingAutomationRule, words: RuleNoteWords): string => {
  const parts = rule.conditions.items.map((cond) => describeCondition(cond, words));
  if (parts.length <= 1) return fill(words.ifSingle, { cond: parts[0] ?? words.emptyValue });
  return rule.conditions.logicalOperator === 'OR'
    ? fill(words.ifAny, { list: joinList(parts, words.listOr) })
    : fill(words.ifAll, { list: joinList(parts, words.listAnd) });
};

export const thenNote = (rule: RankingAutomationRule, words: RuleNoteWords): string => {
  const parts = rule.actions.map((action) => describeAction(action, words));
  const text = fill(words.thenText, { list: joinList(parts, words.listAnd) });
  return rule.stopOnMatch ? `${text} ${words.stopText}` : text;
};
