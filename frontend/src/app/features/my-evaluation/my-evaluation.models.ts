export interface EvaluationPeriodOption {
  readonly id: string;
  readonly name: string;
  readonly isPublished: boolean;
  readonly appealDeadline: string;
  readonly appealDaysRemaining: number;
}

export interface KpiCriteriaItem {
  readonly id: string;
  readonly name: string;
  readonly weightPercent: number;
  readonly actualTargetDisplay: string;
  readonly score: number;
  readonly weightedScore: number;
  readonly hasDrilldown: boolean;
}

export interface TransactionLedgerItem {
  readonly id: string;
  readonly txCode: string;
  readonly date: string;
  readonly amount: string;
  readonly kpiContribution: number;
  readonly status: string;
}

export interface PersonalEvaluationView {
  readonly periodId: string;
  readonly periodName: string;
  readonly status: 'Published' | 'Draft' | 'Finalized';
  readonly appealDeadlineText: string;
  readonly appealRemainingDays: number;
  readonly totalPeScore: number;
  readonly scoreDelta: number;
  readonly previousPeriodName: string;
  readonly previousPeriodScore: number;
  readonly tier: string;
  readonly allTiers: readonly string[];
  readonly rankingPercentile: string;
  readonly rankingPosition: number;
  readonly rankingTotalEmployees: number;
  readonly rankingRoleTitle: string;
  readonly criteria: readonly KpiCriteriaItem[];
  readonly qualityRatio: number;
  readonly riskPenalty: number;
  readonly managerFeedback: string;
  readonly isAcknowledged: boolean;
  readonly acknowledgedAt?: string;
}

export const EVALUATION_PERIODS_MOCK: readonly EvaluationPeriodOption[] = [
  {
    id: '2026-Q2',
    name: 'Quý 2/2026',
    isPublished: true,
    appealDeadline: '23:59 ngày 22/07/2026',
    appealDaysRemaining: 3,
  },
  {
    id: '2026-Q1',
    name: 'Quý 1/2026',
    isPublished: true,
    appealDeadline: '23:59 ngày 22/04/2026',
    appealDaysRemaining: 0,
  },
  {
    id: '2025-Q4',
    name: 'Quý 4/2025',
    isPublished: true,
    appealDeadline: '23:59 ngày 22/01/2026',
    appealDaysRemaining: 0,
  },
];

export const PERSONAL_EVALUATION_MOCK_Q2: PersonalEvaluationView = {
  periodId: '2026-Q2',
  periodName: 'Quý 2/2026',
  status: 'Published',
  appealDeadlineText: '23:59 ngày 22/07/2026',
  appealRemainingDays: 3,
  totalPeScore: 82.0,
  scoreDelta: 3.5,
  previousPeriodName: 'Quý 1/2026',
  previousPeriodScore: 78.5,
  tier: 'A2',
  allTiers: ['A1', 'A2', 'A3', 'B', 'C'],
  rankingPercentile: 'Top 40%',
  rankingPosition: 4,
  rankingTotalEmployees: 11,
  rankingRoleTitle: 'Sales Officer',
  criteria: [
    {
      id: 'crit-deposit',
      name: 'Sales – Tiền gửi – xử lý',
      weightPercent: 40,
      actualTargetDisplay: '4.920.000.000 / 5.500.000.000 VND',
      score: 89.5,
      weightedScore: 35.8,
      hasDrilldown: true,
    },
    {
      id: 'crit-cards',
      name: 'Sales – Mở thẻ – xử lý',
      weightPercent: 20,
      actualTargetDisplay: '40 / 42 hợp đồng',
      score: 95.2,
      weightedScore: 19.0,
      hasDrilldown: true,
    },
    {
      id: 'crit-service',
      name: 'Chất lượng dịch vụ',
      weightPercent: 20,
      actualTargetDisplay: '4,3 / 4,5 điểm',
      score: 95.6,
      weightedScore: 19.1,
      hasDrilldown: true,
    },
    {
      id: 'crit-qualitative',
      name: 'Đánh giá định tính (Trưởng phòng)',
      weightPercent: 20,
      actualTargetDisplay: '—',
      score: 86.0,
      weightedScore: 17.2,
      hasDrilldown: false,
    },
  ],
  qualityRatio: 0.9,
  riskPenalty: 0,
  managerFeedback: 'Dẫn dắt tốt chiến dịch thẻ Q2; cần cải thiện tỷ lệ chốt tiền gửi.',
  isAcknowledged: false,
};

export const PERSONAL_EVALUATION_MOCK_Q1: PersonalEvaluationView = {
  periodId: '2026-Q1',
  periodName: 'Quý 1/2026',
  status: 'Published',
  appealDeadlineText: '23:59 ngày 22/04/2026',
  appealRemainingDays: 0,
  totalPeScore: 78.5,
  scoreDelta: 1.2,
  previousPeriodName: 'Quý 4/2025',
  previousPeriodScore: 77.3,
  tier: 'B',
  allTiers: ['A1', 'A2', 'A3', 'B', 'C'],
  rankingPercentile: 'Top 50%',
  rankingPosition: 6,
  rankingTotalEmployees: 11,
  rankingRoleTitle: 'Sales Officer',
  criteria: [
    {
      id: 'crit-deposit',
      name: 'Sales – Tiền gửi – xử lý',
      weightPercent: 40,
      actualTargetDisplay: '4.200.000.000 / 5.200.000.000 VND',
      score: 80.8,
      weightedScore: 32.3,
      hasDrilldown: true,
    },
    {
      id: 'crit-cards',
      name: 'Sales – Mở thẻ – xử lý',
      weightPercent: 20,
      actualTargetDisplay: '36 / 40 hợp đồng',
      score: 90.0,
      weightedScore: 18.0,
      hasDrilldown: true,
    },
    {
      id: 'crit-service',
      name: 'Chất lượng dịch vụ',
      weightPercent: 20,
      actualTargetDisplay: '4,1 / 4,5 điểm',
      score: 91.1,
      weightedScore: 18.2,
      hasDrilldown: true,
    },
    {
      id: 'crit-qualitative',
      name: 'Đánh giá định tính (Trưởng phòng)',
      weightPercent: 20,
      actualTargetDisplay: '—',
      score: 80.0,
      weightedScore: 16.0,
      hasDrilldown: false,
    },
  ],
  qualityRatio: 0.85,
  riskPenalty: 0,
  managerFeedback: 'Hoàn thành cơ bản chỉ tiêu quý; nỗ lực hơn trong quý tiếp theo.',
  isAcknowledged: true,
  acknowledgedAt: '2026-04-20T10:15:00.000Z',
};

export const MOCK_LEDGER_DATA: Readonly<Record<string, readonly TransactionLedgerItem[]>> = {
  'crit-deposit': [
    {
      id: 'tx-1',
      txCode: 'TX-20260405-001',
      date: '05/04/2026',
      amount: '1.500.000.000 VND',
      kpiContribution: 10.5,
      status: 'VERIFIED',
    },
    {
      id: 'tx-2',
      txCode: 'TX-20260512-088',
      date: '12/05/2026',
      amount: '2.000.000.000 VND',
      kpiContribution: 14.0,
      status: 'VERIFIED',
    },
    {
      id: 'tx-3',
      txCode: 'TX-20260620-142',
      date: '20/06/2026',
      amount: '1.420.000.000 VND',
      kpiContribution: 11.3,
      status: 'VERIFIED',
    },
  ],
  'crit-cards': [
    {
      id: 'tx-4',
      txCode: 'TX-20260410-019',
      date: '10/04/2026',
      amount: '15 hợp đồng thẻ tín dụng',
      kpiContribution: 7.0,
      status: 'VERIFIED',
    },
    {
      id: 'tx-5',
      txCode: 'TX-20260528-044',
      date: '28/05/2026',
      amount: '25 hợp đồng thẻ ghi nợ',
      kpiContribution: 12.0,
      status: 'VERIFIED',
    },
  ],
  'crit-service': [
    {
      id: 'tx-6',
      txCode: 'CS-2026-Q2-AVG',
      date: '30/06/2026',
      amount: '185 lượt đánh giá hài lòng',
      kpiContribution: 19.1,
      status: 'VERIFIED',
    },
  ],
};
