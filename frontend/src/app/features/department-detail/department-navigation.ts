export type DepartmentDestination =
  | 'department-list'
  | 'department-view'
  | 'department-detail'
  | 'employee-list'
  | 'employee-detail'
  | 'department-kpi'
  | 'department-work'
  | 'department-performance';

export interface NavigationContext {
  readonly departmentId?: string;
  readonly employeeId?: string;
  /** KPI / Performance destinations resolve the actual period themselves. */
  readonly period?: 'current';
}

export interface NavigationTarget {
  readonly commands: readonly string[];
  readonly queryParams: Record<string, string>;
}

export const DESTINATION_LABELS: Record<DepartmentDestination, string> = {
  'department-list': 'Danh sách phòng ban',
  'department-view': 'Department View',
  'department-detail': 'Chi tiết phòng ban',
  'employee-list': 'Danh sách nhân sự',
  'employee-detail': 'Hồ sơ cán bộ',
  'department-kpi': 'KPI phòng ban',
  'department-work': 'Công việc phòng ban',
  'department-performance': 'Hiệu suất phòng ban',
};

/**
 * Single place mapping "Current Context → Destination → Passed Context".
 * Returns null for destination screens that do not exist yet.
 */
export function resolveDestination(
  destination: DepartmentDestination,
  context: NavigationContext,
): NavigationTarget | null {
  switch (destination) {
    case 'employee-list': {
      const queryParams: Record<string, string> = {};
      if (context.departmentId) {
        queryParams['departmentId'] = context.departmentId;
      }
      return { commands: ['/employees'], queryParams };
    }
    case 'department-detail':
      return context.departmentId
        ? { commands: ['/departments', context.departmentId], queryParams: {} }
        : null;
    case 'employee-detail': {
      const queryParams: Record<string, string> = {};
      if (context.employeeId) {
        queryParams['employeeId'] = context.employeeId;
      }
      if (context.departmentId) {
        queryParams['departmentId'] = context.departmentId;
      }
      return { commands: ['/employee-profile'], queryParams };
    }
    default:
      return null;
  }
}
