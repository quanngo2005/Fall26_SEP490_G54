import { EvaluationProfileItem } from '../../core/evaluation-profile.models';
import {
  ProfileRow,
  buildItems,
  countRowChanges,
  flattenItems,
  totalWeight,
} from './evaluation-profile-tree';

const leaf = (kpiDefinitionId: string | null, weightPercent: number): EvaluationProfileItem => ({
  id: `leaf-${kpiDefinitionId}`,
  parentItemId: 'g1',
  name: null,
  kpiDefinitionId,
  kpiCode: `CODE-${kpiDefinitionId}`,
  kpiName: `Name ${kpiDefinitionId}`,
  weightPercent,
  displayOrder: 1,
  children: [],
});

const group = (
  name: string | null,
  weightPercent: number,
  children: EvaluationProfileItem[],
): EvaluationProfileItem => ({
  id: `group-${name}`,
  parentItemId: null,
  name,
  kpiDefinitionId: null,
  kpiCode: null,
  kpiName: null,
  weightPercent,
  displayOrder: 1,
  children,
});

describe('evaluation-profile-tree', () => {
  describe('flattenItems', () => {
    it('shows each KPI with its weight in the whole profile (group weight x KPI weight)', () => {
      const { rows, supported } = flattenItems([
        group('Sales', 60, [leaf('k1', 50), leaf('k2', 50)]),
      ]);

      expect(supported).toBeTrue();
      expect(rows.map((row) => [row.group, row.kpiDefinitionId, row.weight])).toEqual([
        ['Sales', 'k1', 30],
        ['Sales', 'k2', 30],
      ]);
    });

    it('keeps a group without KPIs as a row without a KPI', () => {
      const { rows } = flattenItems([group('Quality', 40, [])]);

      expect(rows).toEqual([{ group: 'Quality', kpiDefinitionId: '', weight: 40 }]);
    });

    it('marks a tree the two-level form cannot rebuild as unsupported', () => {
      expect(flattenItems([leaf('k1', 100)]).supported).toBeFalse();
      expect(flattenItems([group('Sales', 100, [group('Nested', 100, [])])]).supported).toBeFalse();
    });
  });

  describe('buildItems', () => {
    it('groups rows by name ignoring case and rebuilds the relative KPI weights', () => {
      const rows: ProfileRow[] = [
        { group: 'Sales', kpiDefinitionId: 'k1', weight: 30 },
        { group: ' sales ', kpiDefinitionId: 'k2', weight: 30 },
        { group: 'Quality', kpiDefinitionId: 'k3', weight: 40 },
      ];

      const items = buildItems(rows);

      expect(items.map((item) => [item.name, item.weightPercent])).toEqual([
        ['Sales', 60],
        ['Quality', 40],
      ]);
      expect(items[0].children?.map((child) => child.weightPercent)).toEqual([50, 50]);
      expect(items[1].children?.map((child) => child.weightPercent)).toEqual([100]);
    });

    it('gives the rounding remainder to the last KPI so a group always totals 100', () => {
      const rows: ProfileRow[] = [1, 2, 3].map((n) => ({
        group: 'Sales',
        kpiDefinitionId: `k${n}`,
        weight: 10,
      }));

      const weights = buildItems(rows)[0].children?.map((child) => child.weightPercent) ?? [];

      expect(weights).toEqual([33.3333, 33.3333, 33.3334]);
    });

    it('skips rows that have no KPI', () => {
      expect(buildItems([{ group: 'Sales', kpiDefinitionId: '', weight: 100 }])).toEqual([]);
    });
  });

  it('adds the weights, treating an empty weight as 0', () => {
    expect(
      totalWeight([
        { group: 'A', kpiDefinitionId: 'k1', weight: 33.3333 },
        { group: 'B', kpiDefinitionId: 'k2', weight: 66.6667 },
        { group: 'C', kpiDefinitionId: 'k3', weight: null },
      ]),
    ).toBe(100);
  });

  it('counts rows that were added, removed, or got a different weight or group', () => {
    const original: ProfileRow[] = [
      { group: 'Sales', kpiDefinitionId: 'k1', weight: 50 },
      { group: 'Sales', kpiDefinitionId: 'k2', weight: 50 },
    ];
    const current: ProfileRow[] = [
      { group: 'Sales', kpiDefinitionId: 'k1', weight: 40 },
      { group: 'Quality', kpiDefinitionId: 'k3', weight: 60 },
    ];

    expect(countRowChanges(original, current)).toBe(3);
    expect(countRowChanges(original, original)).toBe(0);
  });
});
