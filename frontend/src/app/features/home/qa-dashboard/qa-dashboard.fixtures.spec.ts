import { QaDashboard, QaDashboardFilterOptions } from '../../../core/qa-dashboard.models';

/** Days from now as an ISO instant, so deadline tests do not depend on the date they run. */
export function inDays(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  date.setHours(23, 59, 0, 0);
  return date.toISOString();
}

export function dashboardFixture(over: Partial<QaDashboard> = {}): QaDashboard {
  const weeks = [36, 37, 38, 39, 40, 41, 42, 43].map((week, i) => {
    const open = [9, 8, 10, 6, 7, 10, 8, 9][i];
    const total = [195, 211, 192, 200, 200, 170, 200, 204][i];
    return {
      weekLabel: `T${week}`,
      weekNumber: week,
      year: 2026,
      startDate: '2026-08-31',
      endDate: '2026-09-06',
      matchedCount: total - open,
      totalCount: total,
      matchRate: week === 42 ? 96.0 : week === 43 ? 95.6 : 95.0,
    };
  });
  return {
    asOf: '2026-10-26T08:15:02Z',
    period: {
      id: 'p10',
      code: 'PER-2026-10',
      name: '10/2026',
      startDate: '2026-10-01',
      endDate: '2026-10-31',
      lockDeadline: inDays(5),
      isLocked: false,
    },
    banner: { inReviewCount: 12, message: '', warning: '', hasUnmatchedInReview: true },
    kpiCards: {
      notMatched: { count: 7, oldestDays: 4, subtext: '' },
      inReview: { count: 12, subtext: '' },
      qaQueue: { total: 9, sampleCount: 5, flagCount: 4, subtext: '' },
      riskEvents: { count: 3, totalPenaltyPoints: 8, subtext: '' },
    },
    matchedRateChart: {
      title: '',
      subtitle: '',
      currentRate: 95.6,
      currentWeekLabel: 'T43',
      weeks,
    },
    topDiscrepancyEmployees: Array.from({ length: 7 }, (_, i) => ({
      employeeId: `e${i}`,
      employeeCode: `NV02${i}`,
      fullName: `Nhân viên ${i}`,
      jobTitle: 'Teller',
      departmentName: null,
      discrepancyCount: 7 - i,
      flagCount: i % 2,
    })),
    commonDiscrepancyReasons: [
      { reasonCode: 'PRODUCT_MISMATCH', reason: 'Sai sản phẩm', count: 2 },
      { reasonCode: 'NO_IMPORTED_TRANSACTION', reason: 'Chưa có GD import', count: 12 },
      { reasonCode: 'EMPLOYEE_MISMATCH', reason: 'Sai mã nhân viên', count: 3 },
      { reasonCode: 'SOMETHING_NEW', reason: 'Lý do mới', count: 1 },
    ],
    ...over,
  };
}

export const filterOptionsFixture: QaDashboardFilterOptions = {
  periods: [
    { id: 'p10', code: 'PER-2026-10', name: '10/2026', isCurrent: true },
    { id: 'p09', code: 'PER-2026-09', name: '09/2026', isCurrent: false },
  ],
  departments: [{ id: 'd1', code: 'D01', name: 'Phòng Dịch vụ Khách hàng' }],
  jobPositions: [{ id: 'j1', code: 'TELLER', title: 'Teller' }],
};
