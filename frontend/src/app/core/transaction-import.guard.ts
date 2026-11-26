import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { FULL_ACCESS_PERMISSION, normalizePermission } from './organization.guard';

/**
 * `transaction-import:manage` opens the data import screens (SCR-ADM-06 upload, SCR-ADM-07 history). Granted to QA,
 * Verifier, administrators, department and branch managers (UC31). The API enforces the same permission.
 */
export const TRANSACTION_IMPORT_MANAGE_PERMISSION = 'TRANSACTION_IMPORT_MANAGE';

export const canManageTransactionImport = (
  permissions: readonly string[] | null | undefined,
): boolean =>
  (permissions ?? []).some((permission) => {
    const code = normalizePermission(permission);
    return code === TRANSACTION_IMPORT_MANAGE_PERMISSION || code === FULL_ACCESS_PERMISSION;
  });

/**
 * `service_rating_import:manage` adds the "Đánh giá dịch vụ" file type to the data import screen (UC32). Granted to the
 * same roles as the transaction import. The API enforces the same permission.
 */
export const SERVICE_RATING_IMPORT_MANAGE_PERMISSION = 'SERVICE_RATING_IMPORT_MANAGE';

export const canManageServiceRatingImport = (
  permissions: readonly string[] | null | undefined,
): boolean =>
  (permissions ?? []).some((permission) => {
    const code = normalizePermission(permission);
    return code === SERVICE_RATING_IMPORT_MANAGE_PERMISSION || code === FULL_ACCESS_PERMISSION;
  });

/** Sends everybody else to /home. Compose after `authGuard`. */
export const transactionImportGuard: CanActivateFn = () =>
  canManageTransactionImport(inject(AuthService).currentUser()?.permissions) ||
  inject(Router).parseUrl('/home');
