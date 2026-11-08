import { KpiDefinition, KpiPeriodRef, KpiStatus } from '../../core/kpi-definition.models';

/** Keys of `localization.kpi` that name each direction and aggregation. */
export const DIRECTION_KEYS = {
  HIGHER_BETTER: 'higherBetter',
  LOWER_BETTER: 'lowerBetter',
  ON_TARGET: 'onTarget',
} as const;

export const AGGREGATION_KEYS = {
  SUM: 'sum',
  RATIO: 'ratio',
  AVG: 'average',
  NO_AGGREGATION: 'noAggregation',
} as const;

/** A KPI ending within this many days is flagged "expiring soon". */
export const EXPIRING_SOON_DAYS = 90;

export interface ProductGroup {
  /** Null for the KPIs that are not tied to a product. */
  product: string | null;
  items: KpiDefinition[];
}

/** Groups a page of KPIs by product, keeping the order of the API (products alphabetical, KPIs without one last). */
export function groupByProduct(items: readonly KpiDefinition[]): ProductGroup[] {
  const groups: ProductGroup[] = [];
  for (const item of items) {
    const last = groups[groups.length - 1];
    if (last && last.product === item.productName) {
      last.items.push(item);
    } else {
      groups.push({ product: item.productName, items: [item] });
    }
  }
  return groups;
}

/** `2026-Q4` -> `Q4/2026`; any other code is shown as it is. */
export function periodShort(period: Pick<KpiPeriodRef, 'code'>): string {
  const match = /^(\d{4})-Q([1-4])$/.exec(period.code);
  return match ? `Q${match[2]}/${match[1]}` : period.code;
}

/** Whole days from `from` to `to`, both `YYYY-MM-DD`. */
function daysBetween(from: string, to: string): number {
  const day = (value: string) =>
    Date.UTC(+value.slice(0, 4), +value.slice(5, 7) - 1, +value.slice(8, 10));
  return Math.round((day(to) - day(from)) / 86_400_000);
}

/** True for an active KPI whose last period ends within the next 90 days. */
export function isExpiringSoon(
  kpi: Pick<KpiDefinition, 'status' | 'effectiveTo'>,
  today: string,
): boolean {
  if (kpi.status !== 'ACTIVE' || !kpi.effectiveTo) return false;
  const left = daysBetween(today, kpi.effectiveTo.endDate);
  return left >= 0 && left <= EXPIRING_SOON_DAYS;
}

/** The tab a status counts under; a rejected KPI goes back to its author, so it counts as a draft that was returned. */
export type StatusTab = 'ALL' | KpiStatus;

export function summaryCount(
  summary: {
    total: number;
    draft: number;
    pendingApproval: number;
    rejected: number;
    active: number;
    inactive: number;
    archived: number;
  },
  tab: StatusTab,
): number {
  switch (tab) {
    case 'ALL':
      return summary.total;
    case 'DRAFT':
      return summary.draft;
    case 'PENDING_APPROVAL':
      return summary.pendingApproval;
    case 'REJECTED':
      return summary.rejected;
    case 'ACTIVE':
      return summary.active;
    case 'INACTIVE':
      return summary.inactive;
    case 'ARCHIVED':
      return summary.archived;
  }
}

/** What the System Admin can do with a KPI in a given status. */
export interface KpiActions {
  edit: boolean;
  submit: boolean;
  deactivate: boolean;
  reactivate: boolean;
}

export function actionsFor(status: KpiStatus): KpiActions {
  return {
    // A pending KPI is frozen so the approver sees what was submitted; an archived one is closed.
    edit: status !== 'PENDING_APPROVAL' && status !== 'ARCHIVED',
    submit: status === 'DRAFT' || status === 'REJECTED',
    deactivate: status === 'ACTIVE',
    reactivate: status === 'INACTIVE',
  };
}

/** Where the credit comes from, per role and file: the column the system reads to find the employee. */
export function creditSource(
  kpi: Pick<KpiDefinition, 'creditRole' | 'dataSource'>,
): 'REFER_ID' | 'TRANSACTION_EMPLOYEE' | 'RATING_EMPLOYEE' {
  if (kpi.creditRole === 'REFERRER') return 'REFER_ID';
  return kpi.dataSource === 'SERVICE_RATING' ? 'RATING_EMPLOYEE' : 'TRANSACTION_EMPLOYEE';
}

/** The filter set the "save filters" button keeps in the browser. */
export interface SavedFilters {
  keyword: string;
  periodId: string;
  positionId: string;
  creditRole: string;
  dataSource: string;
  groupId: string;
}

export const EMPTY_FILTERS: Readonly<SavedFilters> = Object.freeze({
  keyword: '',
  periodId: '',
  positionId: '',
  creditRole: '',
  dataSource: '',
  groupId: '',
});

const STORAGE_KEY = 'g54.kpi-list.filters';

/** Reads the saved filters; nothing (or anything unreadable) gives the empty set. Storage may be blocked, so every call is guarded. */
export function loadFilters(storage: Pick<Storage, 'getItem'> | null): SavedFilters {
  try {
    const raw = storage?.getItem(STORAGE_KEY);
    if (!raw) return { ...EMPTY_FILTERS };
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== 'object' || parsed === null) return { ...EMPTY_FILTERS };
    const record = parsed as Record<string, unknown>;
    return Object.fromEntries(
      Object.keys(EMPTY_FILTERS).map((key) => [
        key,
        typeof record[key] === 'string' ? record[key] : '',
      ]),
    ) as unknown as SavedFilters;
  } catch {
    return { ...EMPTY_FILTERS };
  }
}

export function saveFilters(
  storage: Pick<Storage, 'setItem' | 'removeItem'> | null,
  filters: SavedFilters,
): boolean {
  try {
    const empty = Object.values(filters).every((value) => value === '');
    if (empty) storage?.removeItem(STORAGE_KEY);
    else storage?.setItem(STORAGE_KEY, JSON.stringify(filters));
    return storage !== null;
  } catch {
    return false;
  }
}
