/** User accounts (SCR-ADM-01/02). See docs/api/USER_ACCOUNT_CONTRACT.md. */
export type UserAccountStatus = 'ACTIVE' | 'LOCKED' | 'EMPLOYEE_TERMINATED';

export interface UserAccountSummary {
  id: string;
  username: string;
  employeeCode: string;
  fullName: string;
  departmentCode: string;
  jobPositionTitle: string | null;
  roleId: string;
  roleName: string;
  status: UserAccountStatus;
  lastLoginAt: string | null;
}

export interface UserAccountHistoryEntry {
  occurredAt: string;
  event: string;
  actor: string;
}

export interface UserAccountDetail extends UserAccountSummary {
  employeeId: string;
  departmentName: string;
  suggestedRoleId: string | null;
  createdAt: string;
  createdBy: string;
  passwordChangedAt: string | null;
  history: UserAccountHistoryEntry[];
}

export interface UserAccountFilter {
  keyword?: string;
  roleId?: string;
  departmentId?: string;
  status?: UserAccountStatus;
  page?: number;
  pageSize?: number;
}

export interface UserAccountSummaryCounts {
  activeCount: number;
  lockedCount: number;
  employeesWithoutAccountCount: number;
}

export interface EligibleEmployee {
  id: string;
  employeeCode: string;
  fullName: string;
  departmentCode: string;
  departmentName: string;
  jobPositionTitle: string | null;
  email: string;
  source: string;
  suggestedRoleId: string | null;
  suggestedUsername: string | null;
}

export interface CreateUserAccountPayload {
  employeeId: string;
  username: string;
  roleId: string;
  mustChangePassword: boolean;
}

export interface CreateUserAccountResult {
  account: UserAccountDetail;
  temporaryPassword: string;
}

export interface UpdateUserAccountPayload {
  roleId: string;
}
