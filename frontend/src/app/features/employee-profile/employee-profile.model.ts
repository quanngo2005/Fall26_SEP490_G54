export type SemanticTone = 'blue' | 'green' | 'amber' | 'red' | 'neutral' | 'navy';

export interface EmployeeNavigationItem {
  label: string;
  icon: string;
  count?: string;
}

export interface EmployeeNavigationGroup {
  title: string;
  items: EmployeeNavigationItem[];
}

export interface WorkPreviewItem {
  id: string;
  title: string;
  metadata: string;
  priority: string;
  priorityTone: SemanticTone;
  deadline: string;
  deadlineNote: string;
  status: string;
  statusTone: SemanticTone;
  progress: number;
  progressTone: SemanticTone;
}

export interface EmployeeKpiItem {
  title: string;
  code: string;
  weight: string;
  target: string;
  actual: string;
  progress: number;
  progressText: string;
  source: string;
  tone: SemanticTone;
}

export interface PerformanceReview {
  period: string;
  state: string;
  stateTone: SemanticTone;
  score: string;
  grade: string;
  gradeTone: SemanticTone;
  status?: string;
  description: string;
  managerNote?: string;
}

export interface AuditEvent {
  time: string;
  kind: string;
  title: string;
  detail: string;
  source?: string;
  tone: SemanticTone;
}

export interface EmployeeProfilePageData {
  employee: {
    id: string;
    name: string;
    title: string;
    roleCode: string;
    department: string;
    manager: string;
    email: string;
    extension: string;
    contract: string;
    status: string;
    branchCode: string;
  };
  navigation: EmployeeNavigationGroup[];
  workPreview: WorkPreviewItem[];
  kpis: EmployeeKpiItem[];
  reviews: PerformanceReview[];
  auditLog: AuditEvent[];
}
