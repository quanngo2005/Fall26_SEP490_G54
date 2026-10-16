export type EmployeeStatus = 'active' | 'on_leave' | 'probation' | 'inactive';

export interface EmployeeListItem {
  readonly id: string;
  readonly code: string;
  readonly fullName: string;
  readonly avatarUrl?: string;
  readonly email: string;
  readonly departmentId: string;
  readonly departmentName: string;
  readonly departmentCode: string;
  readonly positionId: string;
  readonly positionTitle: string;
  readonly positionCode: string;
  readonly level: string;
  readonly managerId?: string;
  readonly managerCode?: string;
  readonly managerName?: string;
  readonly managerPosition?: string;
  readonly activeWorkCount: number;
  readonly status: EmployeeStatus;
  readonly statusLabel: string;
}

export interface EmployeeSummaryMetrics {
  readonly total: number;
  readonly targetQuota: number;
  readonly quotaPercent: number;
  readonly active: number;
  readonly activePercent: number;
  readonly onLeave: number;
  readonly onLeaveMaternity: number;
  readonly onLeaveAnnual: number;
  readonly onLeavePercent: number;
  readonly probation: number;
  readonly probationApprentice: number;
  readonly probationContractPending: number;
}

export interface PositionDistributionItem {
  readonly positionId: string;
  readonly title: string;
  readonly count: number;
}

export interface EmployeeFilterOptions {
  readonly departments: readonly {
    readonly id: string;
    readonly name: string;
    readonly code: string;
  }[];
  readonly positions: readonly { readonly id: string; readonly name: string }[];
  readonly managers: readonly { readonly id: string; readonly name: string }[];
  readonly statuses: readonly { readonly value: EmployeeStatus; readonly label: string }[];
}

export interface EmployeeListQuery {
  readonly search?: string;
  readonly departmentId?: string;
  readonly positionId?: string;
  readonly status?: string;
  readonly managerId?: string;
  readonly page?: number;
  readonly pageSize?: number;
  readonly sortBy?: string;
  readonly sortDirection?: 'asc' | 'desc';
}

export interface EmployeeListResponse {
  readonly items: readonly EmployeeListItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly metrics: EmployeeSummaryMetrics;
  readonly positionDistribution: readonly PositionDistributionItem[];
  readonly filterOptions: EmployeeFilterOptions;
  readonly scopeInfo: {
    readonly scopeName: string;
    readonly description: string;
    readonly syncTime: string;
  };
}
