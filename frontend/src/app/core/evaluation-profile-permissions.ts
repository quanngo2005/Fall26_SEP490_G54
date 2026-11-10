import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';

/**
 * `GET` needs `kpi:read`; writes need `kpi:manage` (ADMIN only in the seed); approve and reject need
 * `evaluation-profile:approve` (BRANCH_MANAGER). The API enforces them; these only decide which actions to show.
 */
const MANAGE_PERMISSION = 'KPI_MANAGE';
const APPROVE_PERMISSION = 'EVALUATION_PROFILE_APPROVE';

function has(permissions: readonly string[] | null | undefined, wanted: string): boolean {
  return (permissions ?? []).some((permission) => {
    const code = normalizePermission(permission);
    return code === wanted || code === FULL_ACCESS_PERMISSION;
  });
}

export const canManageEvaluationProfiles = (permissions: readonly string[] | null | undefined) =>
  has(permissions, MANAGE_PERMISSION);

export const canApproveEvaluationProfiles = (permissions: readonly string[] | null | undefined) =>
  has(permissions, APPROVE_PERMISSION);
