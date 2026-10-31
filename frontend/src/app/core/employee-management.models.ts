export type EmployeeWorkStatus =
  'ACTIVE' | 'INACTIVE' | 'MISSING_IN_FILE' | 'TERMINATED' | 'ON_LEAVE';

export interface EmployeeListItem {
  id: string;
  code: string;
  fullName: string;
  departmentCode: string;
  departmentName: string;
  positionCode: string;
  positionTitle: string;
  status: EmployeeWorkStatus;
  account: string;
  managerName: string;
  joinDate: string;
  email: string;
  phone: string;
  lastSyncAt: string;
  latestLockedEvaluation?: {
    period: string;
    tier: string;
  };
  latestImportBatch?: {
    batchNumber: string;
    timestamp: string;
    changeStatus: string;
  };
}
