import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { catchError, EMPTY, finalize, switchMap, tap } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { RouteStateService } from '../../core/route-state.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  ReconciliationDetail,
  ReconciliationField,
  ReconciliationHistory,
  ReconciliationSibling,
} from '../../core/reconciliation.models';
import { ReconciliationService } from '../../core/reconciliation.service';
import { normalizePermission } from '../../core/organization.guard';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppDatePipe } from '../../shared/pipes';
import { StatusBadgeComponent, StatusBadgeTone } from '../../shared/ui';
import { RiskEventCreateModalComponent } from '../risk-events/risk-event-create-modal.component';
import { hoursLeft, reconciliationErrorKey, statusTone } from './reconciliation-display';
import { ReconciliationFlagModalComponent } from './reconciliation-flag-modal.component';

type ResultKind = 'match' | 'mismatch' | 'fromFile' | 'waiting' | 'notApplicable';

/** SCR-REV-03-D1: one transaction code compared field by field with the imported Core Banking file. */
@Component({
  selector: 'app-reconciliation-detail',
  imports: [
    RouterLink,
    SidebarComponent,
    HeaderComponent,
    StatusBadgeComponent,
    AppDatePipe,
    ReconciliationFlagModalComponent,
    RiskEventCreateModalComponent,
  ],
  templateUrl: './reconciliation-detail.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './reconciliation-detail.component.scss',
  ],
})
export class ReconciliationDetailComponent extends LocalizedComponent {
  private readonly api = inject(ReconciliationService);
  private readonly routeState = inject(RouteStateService);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly detail = signal<ReconciliationDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly flagging = signal(false);
  protected readonly recordingRisk = signal(false);
  protected readonly notice = signal<string | null>(null);

  /** Only people who decide on results can open an appeal screen. */
  protected readonly canOpenAppeal = computed(() =>
    (this.auth.currentUser()?.permissions ?? []).some(
      (code) => normalizePermission(code) === 'VERIFICATION_DECIDE',
    ),
  );
  protected readonly canAct = computed(() => {
    const actions = this.detail()?.actions;
    return (
      !!actions &&
      (actions.canFlag ||
        actions.canRecordRisk ||
        (!!actions.flagBlockedReason && actions.flagBlockedReason !== 'NO_PERMISSION'))
    );
  });
  protected readonly riskPreset = computed(() => {
    const d = this.detail();
    if (!d) return null;
    const item = d.item;
    return {
      employeeId: item.employeeId,
      employeeCode: item.employeeCode,
      employeeName: item.employeeName,
      workCode: item.workCode,
      description: this.i18n.translate('reconciliation.riskPrefill', {
        code: item.transactionCode,
        reason: this.reasonLabel(item.reason),
      }),
    };
  });

  constructor() {
    super();
    this.routeState
      .watch('reconciliationId')
      .pipe(
        switchMap((id) => {
          this.loading.set(true);
          this.errorKey.set(null);
          return this.api.detail(id ?? '').pipe(
            tap((detail) => this.detail.set(detail)),
            catchError((error: unknown) => {
              this.detail.set(null);
              this.errorKey.set(reconciliationErrorKey(error));
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  protected tone(status: ReconciliationDetail['item']['status']): StatusBadgeTone {
    return statusTone(status);
  }

  protected statusLabel(status: ReconciliationDetail['item']['status']): string {
    return this.localization.reconciliation.status[status];
  }

  protected reasonLabel(reason: string | null): string {
    if (!reason) return '';
    return (this.localization.reconciliation.reasons as Record<string, string>)[reason] ?? reason;
  }

  protected fieldLabel(field: ReconciliationField): string {
    return this.localization.reconciliation.detail.fields[field.field];
  }

  /** How a field's verdict reads: a missing file keeps every row at "waiting", the amount is simply taken from the file. */
  protected resultKind(field: ReconciliationField, hasImport: boolean): ResultKind {
    if (field.matched === true) return 'match';
    if (field.matched === false) return 'mismatch';
    if (!hasImport) return 'waiting';
    return field.field === 'AMOUNT' ? 'fromFile' : 'notApplicable';
  }

  protected resultLabel(kind: ResultKind): string {
    return this.localization.reconciliation.detail.result[kind];
  }

  protected declaredText(field: ReconciliationField): string {
    const text = this.localization.reconciliation.detail;
    if (field.field === 'AMOUNT') return text.declaredAmount;
    if (field.declared !== null) return field.declared;
    return field.field === 'REFERRAL' ? text.noHandover : text.notDeclared;
  }

  protected importedText(field: ReconciliationField, hasImport: boolean): string {
    const text = this.localization.reconciliation.detail;
    if (!hasImport) return text.notInFile;
    if (field.field === 'AMOUNT' && field.imported !== null) {
      return `${this.i18n.formatNumber(Number(field.imported))} VND`;
    }
    return field.imported ?? text.emptyInFile;
  }

  protected summaryText(d: ReconciliationDetail): string {
    return this.i18n.translate('reconciliation.detail.summary', {
      matched: d.matchedFields,
      compared: d.comparedFields,
    });
  }

  protected historyText(entry: ReconciliationHistory): string {
    const events = this.localization.reconciliation.history.event;
    const actor = entry.actor ?? this.localization.reconciliation.history.system;
    switch (entry.kind) {
      case 'CODE_ADDED':
        return this.i18n.translate('reconciliation.history.event.CODE_ADDED', { actor });
      case 'IMPORTED':
        return this.i18n.translate(
          entry.actor
            ? 'reconciliation.history.event.IMPORTED_BY'
            : 'reconciliation.history.event.IMPORTED',
          { reference: entry.reference ?? '', actor },
        );
      case 'RECONCILED':
        if (entry.status === 'MATCHED') return events.RECONCILED_MATCHED;
        if (entry.status === 'NOT_MATCHED') {
          return this.i18n.translate('reconciliation.history.event.RECONCILED_NOT_MATCHED', {
            reason: this.reasonLabel(entry.reason),
          });
        }
        return events.RECONCILED_IN_REVIEW;
      case 'QUALITY_FLAG':
        return this.i18n.translate('reconciliation.history.event.QUALITY_FLAG', {
          actor,
          reference: entry.reference ?? '',
        });
      case 'RISK_EVENT':
        return this.i18n.translate('reconciliation.history.event.RISK_EVENT', {
          actor,
          reference: entry.reference ?? '',
        });
      default:
        return events.APPEAL;
    }
  }

  protected historyTone(entry: ReconciliationHistory): 'danger' | 'success' | 'neutral' {
    if (entry.kind === 'RECONCILED') {
      return entry.status === 'NOT_MATCHED'
        ? 'danger'
        : entry.status === 'MATCHED'
          ? 'success'
          : 'neutral';
    }
    return entry.kind === 'QUALITY_FLAG' || entry.kind === 'RISK_EVENT' ? 'danger' : 'neutral';
  }

  protected appealText(d: ReconciliationDetail): string {
    const appeal = d.appeal;
    const deadline = appeal.deadline
      ? this.i18n.formatDate(appeal.deadline, 'DD/MM/YYYY HH:mm')
      : '';
    switch (appeal.state) {
      case 'AVAILABLE':
        return this.i18n.translate('reconciliation.appeal.text.AVAILABLE', {
          hours: appeal.deadline ? hoursLeft(appeal.deadline) : 0,
          deadline,
        });
      case 'EXPIRED':
        return this.i18n.translate('reconciliation.appeal.text.EXPIRED', { deadline });
      case 'DECIDED':
        return this.i18n.translate('reconciliation.appeal.text.DECIDED', {
          status:
            (this.localization.reconciliation.appeal.status as Record<string, string>)[
              appeal.status ?? ''
            ] ?? '',
        });
      default:
        return this.localization.reconciliation.appeal.text[appeal.state];
    }
  }

  protected flagBlockedText(reason: string | null): string {
    return reason
      ? ((this.localization.reconciliation.detail.flagBlocked as Record<string, string>)[reason] ??
          '')
      : '';
  }

  protected openSibling(sibling: ReconciliationSibling): void {
    void this.router.navigate(['/reconciliations/detail'], {
      state: { reconciliationId: sibling.id },
    });
  }

  protected onFlagged(): void {
    this.flagging.set(false);
    this.notice.set(this.localization.reconciliation.flagModal.success);
    this.reloadCurrent();
  }

  protected onRiskRecorded(result: { code: string; uploadFailed: boolean }): void {
    this.recordingRisk.set(false);
    this.notice.set(this.i18n.translate('reconciliation.riskCreated', { code: result.code }));
    this.reloadCurrent();
  }

  private reloadCurrent(): void {
    const id = this.detail()?.item.id;
    if (!id) return;
    this.api
      .detail(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (detail) => this.detail.set(detail), error: () => undefined });
  }
}
