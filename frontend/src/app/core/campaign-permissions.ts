import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';

/**
 * `campaign:manage` imports, allocates, completes and cancels; `campaign_customer:update` updates my own
 * customers. The API enforces both; these only decide which actions to show.
 */
function has(permissions: readonly string[] | null | undefined, wanted: string): boolean {
  return (permissions ?? []).some((permission) => {
    const code = normalizePermission(permission);
    return code === wanted || code === FULL_ACCESS_PERMISSION;
  });
}

export const canManageCampaigns = (permissions: readonly string[] | null | undefined) =>
  has(permissions, 'CAMPAIGN_MANAGE');
