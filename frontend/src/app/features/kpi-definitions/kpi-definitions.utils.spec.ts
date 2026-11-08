import { KpiDefinition, KpiPeriodRef } from '../../core/kpi-definition.models';
import {
  EMPTY_FILTERS,
  actionsFor,
  creditSource,
  groupByProduct,
  isExpiringSoon,
  loadFilters,
  periodShort,
  saveFilters,
  summaryCount,
} from './kpi-definitions.utils';

const kpi = (code: string, extra: Partial<KpiDefinition> = {}): KpiDefinition =>
  ({
    id: code,
    code,
    name: `KPI ${code}`,
    status: 'ACTIVE',
    productName: null,
    creditRole: 'PROCESSOR',
    dataSource: 'TRANSACTION',
    effectiveFrom: null,
    effectiveTo: null,
    ...extra,
  }) as KpiDefinition;

const period = (code: string, endDate: string): KpiPeriodRef => ({
  id: code,
  code,
  name: code,
  startDate: '2026-01-01',
  endDate,
});

describe('kpi-definitions utils', () => {
  describe('groupByProduct', () => {
    it('keeps the order of the API and starts a group whenever the product changes', () => {
      const groups = groupByProduct([
        kpi('A', { productName: 'Thẻ' }),
        kpi('B', { productName: 'Thẻ' }),
        kpi('C', { productName: 'Tiền gửi' }),
        kpi('D'),
        kpi('E'),
      ]);

      expect(groups.map((g) => [g.product, g.items.map((i) => i.code)])).toEqual([
        ['Thẻ', ['A', 'B']],
        ['Tiền gửi', ['C']],
        [null, ['D', 'E']],
      ]);
    });

    it('gives no group for no KPI', () => {
      expect(groupByProduct([])).toEqual([]);
    });
  });

  describe('periodShort', () => {
    it('turns a quarter code into Qn/YYYY and leaves any other code alone', () => {
      expect(periodShort({ code: '2026-Q4' })).toBe('Q4/2026');
      expect(periodShort({ code: '2026-10' })).toBe('2026-10');
    });
  });

  describe('isExpiringSoon', () => {
    const base = { status: 'ACTIVE' as const };

    it('flags an active KPI that ends within 90 days', () => {
      expect(
        isExpiringSoon({ ...base, effectiveTo: period('2026-Q4', '2026-12-31') }, '2026-10-24'),
      ).toBeTrue();
      expect(
        isExpiringSoon({ ...base, effectiveTo: period('2026-Q4', '2026-12-31') }, '2026-12-31'),
      ).toBeTrue();
    });

    it('does not flag a KPI that ends later, already ended, has no end, or is not active', () => {
      expect(
        isExpiringSoon({ ...base, effectiveTo: period('2027-Q4', '2027-12-31') }, '2026-10-24'),
      ).toBeFalse();
      expect(
        isExpiringSoon({ ...base, effectiveTo: period('2026-Q3', '2026-09-30') }, '2026-10-24'),
      ).toBeFalse();
      expect(isExpiringSoon({ ...base, effectiveTo: null }, '2026-10-24')).toBeFalse();
      expect(
        isExpiringSoon(
          { status: 'DRAFT', effectiveTo: period('2026-Q4', '2026-12-31') },
          '2026-10-24',
        ),
      ).toBeFalse();
    });
  });

  describe('summaryCount', () => {
    const summary = {
      total: 24,
      draft: 1,
      pendingApproval: 2,
      rejected: 3,
      active: 18,
      inactive: 4,
      archived: 5,
    };

    it('reads the count of each tab', () => {
      expect(
        (
          [
            'ALL',
            'DRAFT',
            'PENDING_APPROVAL',
            'REJECTED',
            'ACTIVE',
            'INACTIVE',
            'ARCHIVED',
          ] as const
        ).map((tab) => summaryCount(summary, tab)),
      ).toEqual([24, 1, 2, 3, 18, 4, 5]);
    });
  });

  describe('actionsFor', () => {
    it('lets the System Admin edit and send only what is not frozen', () => {
      expect(actionsFor('DRAFT')).toEqual({
        edit: true,
        submit: true,
        deactivate: false,
        reactivate: false,
      });
      expect(actionsFor('REJECTED')).toEqual({
        edit: true,
        submit: true,
        deactivate: false,
        reactivate: false,
      });
      expect(actionsFor('PENDING_APPROVAL')).toEqual({
        edit: false,
        submit: false,
        deactivate: false,
        reactivate: false,
      });
      expect(actionsFor('ACTIVE')).toEqual({
        edit: true,
        submit: false,
        deactivate: true,
        reactivate: false,
      });
      expect(actionsFor('INACTIVE')).toEqual({
        edit: true,
        submit: false,
        deactivate: false,
        reactivate: true,
      });
      expect(actionsFor('ARCHIVED').edit).toBeFalse();
    });
  });

  describe('creditSource', () => {
    it('names where the system finds the employee', () => {
      expect(creditSource({ creditRole: 'REFERRER', dataSource: 'TRANSACTION' })).toBe('REFER_ID');
      expect(creditSource({ creditRole: 'PROCESSOR', dataSource: 'TRANSACTION' })).toBe(
        'TRANSACTION_EMPLOYEE',
      );
      expect(creditSource({ creditRole: 'PROCESSOR', dataSource: 'SERVICE_RATING' })).toBe(
        'RATING_EMPLOYEE',
      );
    });
  });

  describe('saved filters', () => {
    const memory = (initial: Record<string, string> = {}) => {
      const data = { ...initial };
      return {
        data,
        getItem: (key: string) => data[key] ?? null,
        setItem: (key: string, value: string) => void (data[key] = value),
        removeItem: (key: string) => void delete data[key],
      };
    };

    it('stores the filters and reads them back', () => {
      const storage = memory();
      const filters = { ...EMPTY_FILTERS, keyword: 'card', creditRole: 'REFERRER' };

      expect(saveFilters(storage, filters)).toBeTrue();

      expect(loadFilters(storage)).toEqual(filters);
    });

    it('forgets the saved filters when saving an empty set', () => {
      const storage = memory();
      saveFilters(storage, { ...EMPTY_FILTERS, keyword: 'x' });

      saveFilters(storage, EMPTY_FILTERS);

      expect(Object.keys(storage.data)).toEqual([]);
      expect(loadFilters(storage)).toEqual(EMPTY_FILTERS);
    });

    it('treats missing, broken or foreign data as no filters', () => {
      expect(loadFilters(null)).toEqual(EMPTY_FILTERS);
      expect(loadFilters(memory())).toEqual(EMPTY_FILTERS);
      expect(loadFilters(memory({ 'g54.kpi-list.filters': 'not json' }))).toEqual(EMPTY_FILTERS);
      expect(loadFilters(memory({ 'g54.kpi-list.filters': '5' }))).toEqual(EMPTY_FILTERS);
      expect(
        loadFilters(memory({ 'g54.kpi-list.filters': '{"keyword":7,"groupId":"g"}' })),
      ).toEqual({
        ...EMPTY_FILTERS,
        groupId: 'g',
      });
    });

    it('reports that nothing could be saved when storage is unavailable or throws', () => {
      expect(saveFilters(null, { ...EMPTY_FILTERS, keyword: 'x' })).toBeFalse();
      const broken = {
        getItem: () => {
          throw new Error('blocked');
        },
        setItem: () => {
          throw new Error('blocked');
        },
        removeItem: () => {
          throw new Error('blocked');
        },
      };
      expect(saveFilters(broken, { ...EMPTY_FILTERS, keyword: 'x' })).toBeFalse();
      expect(loadFilters(broken)).toEqual(EMPTY_FILTERS);
    });
  });
});
