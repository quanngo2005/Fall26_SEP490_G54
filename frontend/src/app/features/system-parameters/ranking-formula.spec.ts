import localizationData from '../../core/i18n/localization.json';
import { RankingAutomationRule } from '../../core/system-parameter.models';
import { describeFormula, parseFormula } from './ranking-formula';
import { ifNote, thenNote, whenNote } from './ranking-rule-notes';

describe('ranking formula explanation', () => {
  const vi = localizationData.translations.vi.rankingStudio;
  const variables = ['TotalScore', 'QualitativeScore', 'DepartmentAvgScore', 'RiskPenalty'];
  const label = (name: string) => (vi.formula.variables as Record<string, string>)[name] ?? name;
  const explain = (formula: string) =>
    describeFormula(parseFormula(formula, variables)!, vi.formula, label);

  it('GB910_FE14 parses formulas with the backend grammar', () => {
    expect(parseFormula('if(totalscore>=70 && RiskPenalty<1,TotalScore,-5)', variables)).toEqual({
      kind: 'fn',
      name: 'IF',
      args: [
        {
          kind: 'logic',
          op: 'AND',
          left: {
            kind: 'cmp',
            op: '>=',
            left: { kind: 'var', name: 'TotalScore' },
            right: { kind: 'num', value: 70 },
          },
          right: {
            kind: 'cmp',
            op: '<',
            left: { kind: 'var', name: 'RiskPenalty' },
            right: { kind: 'num', value: 1 },
          },
        },
        { kind: 'var', name: 'TotalScore' },
        { kind: 'num', value: -5 },
      ],
    });
  });

  it('GB910_FE15 returns null for formulas with a syntax error', () => {
    expect(parseFormula('', variables)).toBeNull();
    expect(parseFormula('TotalScore +', variables)).toBeNull();
    expect(parseFormula('IF(TotalScore > 1, 2)', variables)).toBeNull();
    expect(parseFormula('SQRT(TotalScore)', variables)).toBeNull();
  });

  it('GB910_FE16 reads the formula as one plain Vietnamese sentence', () => {
    expect(explain('TotalScore')).toBe('Lấy tổng điểm.');
    expect(explain('IF(DepartmentAvgScore < 70, ROUND(TotalScore * 0.8, 2), TotalScore)')).toBe(
      'Nếu điểm trung bình của đơn vị nhỏ hơn 70 thì lấy tổng điểm nhân 0,8, ' +
        'làm tròn lấy 2 chữ số thập phân, ngược lại lấy tổng điểm.',
    );
    expect(explain('CLAMP(MIN(TotalScore, QualitativeScore + 12), 0, 100)')).toBe(
      'Lấy tổng điểm, nhưng không vượt quá điểm định tính cộng 12; ' +
        'sau cùng giữ trong khoảng từ 0 đến 100.',
    );
    expect(
      explain('CLAMP(IF(DepartmentAvgScore < 70, TotalScore * 0.8, TotalScore), 0, 100)'),
    ).toBe(
      'Nếu điểm trung bình của đơn vị nhỏ hơn 70 thì lấy tổng điểm nhân 0,8, ' +
        'ngược lại lấy tổng điểm; sau cùng giữ trong khoảng từ 0 đến 100.',
    );
    expect(explain('(TotalScore + QualitativeScore) / 2 - RiskPenalty')).toBe(
      'Lấy tổng điểm cộng điểm định tính, rồi chia cho 2, rồi trừ điểm trừ do rủi ro.',
    );
    expect(explain('TotalScore - RiskPenalty * 2')).toBe(
      'Lấy tổng điểm trừ kết quả của điểm trừ do rủi ro nhân 2.',
    );
  });

  it('GB910_FE18 writes a plain-language note under the WHEN, IF and THEN blocks', () => {
    const rule: RankingAutomationRule = {
      id: 'r1',
      name: 'Rủi ro nghiêm trọng',
      enabled: true,
      stopOnMatch: true,
      conditions: {
        logicalOperator: 'AND',
        items: [
          { metric: 'MAX_RISK_SEVERITY', operator: 'GTE', value: 4 },
          { metric: 'CURRENT_TIER', operator: 'IN', values: ['A1', 'A2'] },
        ],
        groups: [],
      },
      actions: [
        { type: 'CAP_MAX_TIER', tierCode: 'B' },
        { type: 'SET_COACHING_FLAG', flagValue: true },
      ],
    };
    expect(whenNote(rule, vi.notes)).toBe(vi.notes.when);
    expect(ifNote(rule, vi.notes)).toBe(
      'Nếu đồng thời mức rủi ro cao nhất lớn hơn hoặc bằng 4, tức mức nghiêm trọng ' +
        'và hạng tạm tính hiện tại là A1 hoặc A2.',
    );
    expect(thenNote(rule, vi.notes)).toBe(
      'Thì chỉ cho xếp hạng cao nhất là B và đánh dấu nhân viên cần được kèm cặp. ' +
        vi.notes.stopText,
    );
  });
});
