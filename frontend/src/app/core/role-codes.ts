/**
 * The six roles of the system, one per actor, exactly as the API sends them in `user.roles`. There are no aliases:
 * the database, the JWT role claims and the sidebar all use these codes (`RoleCodes` in the backend is the twin of this file).
 */
export const ROLE = {
  employee: 'EMPLOYEE',
  deptManager: 'DEPT_MANAGER',
  branchManager: 'BRANCH_MANAGER',
  hrOfficer: 'HR_OFFICER',
  verifier: 'VERIFIER',
  systemAdmin: 'SYSTEM_ADMIN',
} as const;

export type RoleCode = (typeof ROLE)[keyof typeof ROLE];

/** `dept-manager`, `Dept Manager` and `DEPT_MANAGER` are the same role code. */
export function normalizeRole(role: string | null | undefined): string {
  return (role ?? '')
    .trim()
    .toUpperCase()
    .replace(/[.:\-\s]+/g, '_');
}

export function normalizeRoles(roles: readonly string[] | null | undefined): string[] {
  return (roles ?? []).map(normalizeRole);
}

export const hasRole = (
  roles: readonly string[] | null | undefined,
  ...wanted: RoleCode[]
): boolean => normalizeRoles(roles).some((role) => (wanted as readonly string[]).includes(role));
