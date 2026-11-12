/** System parameters (SCR-ADM-04). See docs/api/SYSTEM_PARAMETERS_CONTRACT.md. */
export const SYSTEM_PARAMETER_KEYS = [
  'manualApprovalHours',
  'importMaxFileMb',
  'importFormats',
  'kpiLockTime',
  'appealWindowDays',
  'preCloseCheckDays',
  'appealRejectedHours',
  'handoverThresholdVnd',
] as const;
export type SystemParameterKey = (typeof SYSTEM_PARAMETER_KEYS)[number];

export interface SystemParameter {
  key: SystemParameterKey;
  value: number | string;
  min: number | null;
  max: number | null;
  locked: boolean;
  updatedAt: string | null;
  updatedBy: string | null;
}

export interface SystemParameterChange {
  key: SystemParameterKey;
  value: number;
}

export interface SystemParametersSavePayload {
  changes: SystemParameterChange[];
}
