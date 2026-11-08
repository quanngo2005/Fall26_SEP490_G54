import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { BranchEvaluationPeriodItem, FinalizePeriodRequest } from '../../core/evaluation.models';
import { EvaluationService } from '../../core/evaluation.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import { AppDatePipe, AppNumberPipe } from '../../shared/pipes';

const TOAST_DURATION_MS = 4000;

@Component({
  selector: 'app-branch-evaluation-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    AppDatePipe,
    AppNumberPipe,
  ],
  templateUrl: './branch-evaluation-list.component.html',
  styleUrls: ['./branch-evaluation-list.component.scss'],
})
export class BranchEvaluationListComponent extends LocalizedComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly evaluationService = inject(EvaluationService);
  private readonly destroyRef = inject(DestroyRef);
  private periodsRequest?: Subscription;
  private toastTimer?: ReturnType<typeof setTimeout>;

  readonly branchName = signal<string | null>(null);
  readonly periods = signal<BranchEvaluationPeriodItem[]>([]);
  readonly serverYears = signal<number[]>([]);
  readonly loading = signal<boolean>(true);
  readonly failure = signal<'offline' | 'forbidden' | 'error' | null>(null);

  readonly selectedYear = signal<number | 'ALL'>(new Date().getFullYear());
  readonly selectedPeriodType = signal<string>('ALL');
  readonly selectedStatus = signal<string>('ALL');

  readonly isLockModalOpen = signal<boolean>(false);
  readonly periodToLock = signal<BranchEvaluationPeriodItem | null>(null);
  readonly lockSignatureToken = signal<string>('');
  readonly lockApprovalComments = signal<string>('');
  readonly isLocking = signal<boolean>(false);
  readonly lockError = signal<string | null>(null);
  readonly toastMessage = signal<string | null>(null);

  readonly availableYears = computed(() => {
    const years = new Set<number>(this.serverYears());
    const selected = this.selectedYear();
    if (selected !== 'ALL') {
      years.add(selected);
    }
    return Array.from(years).sort((a, b) => b - a);
  });

  readonly headerTitle = computed(() => {
    return this.i18n.translate('evaluations.branchPeriods.headerTitle', {
      branch: this.branchName() ?? this.localization.evaluations.emptyValue,
    });
  });

  readonly errorMessage = computed(() => {
    const text = this.localization.evaluations;
    switch (this.failure()) {
      case 'offline':
        return text.offline;
      case 'forbidden':
        return text.forbidden;
      case 'error':
        return text.branchPeriods.errorTitle;
      default:
        return null;
    }
  });

  ngOnInit(): void {
    this.loadData();
    this.destroyRef.onDestroy(() => clearTimeout(this.toastTimer));
  }

  loadData(): void {
    this.periodsRequest?.unsubscribe();
    this.loading.set(true);
    this.failure.set(null);

    const year = this.selectedYear();
    this.periodsRequest = this.evaluationService
      .branchPeriods(year === 'ALL' ? null : year, this.selectedPeriodType(), this.selectedStatus())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (overview) => {
          this.branchName.set(overview.branchName || null);
          this.serverYears.set(overview.availableYears ?? []);
          this.periods.set(overview.periods ?? []);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          const status = error instanceof HttpErrorResponse ? error.status : null;
          this.periods.set([]);
          this.failure.set(status === 0 ? 'offline' : status === 403 ? 'forbidden' : 'error');
          this.loading.set(false);
        },
      });
  }

  onYearChange(value: string): void {
    this.selectedYear.set(value === 'ALL' ? 'ALL' : Number(value));
    this.loadData();
  }

  onPeriodTypeChange(value: string): void {
    this.selectedPeriodType.set(value);
    this.loadData();
  }

  onStatusChange(value: string): void {
    this.selectedStatus.set(value);
    this.loadData();
  }

  openPeriod(period: BranchEvaluationPeriodItem): void {
    this.router.navigate(['/branch-performance/evaluations', period.id, 'approval']);
  }

  viewHistory(period: BranchEvaluationPeriodItem): void {
    this.router.navigate(['/branch-performance/evaluations', period.id, 'approval']);
  }

  openLockModal(period: BranchEvaluationPeriodItem): void {
    this.periodToLock.set(period);
    this.lockSignatureToken.set('');
    this.lockApprovalComments.set('');
    this.lockError.set(null);
    this.isLockModalOpen.set(true);
  }

  closeLockModal(): void {
    this.isLockModalOpen.set(false);
    this.periodToLock.set(null);
    this.lockError.set(null);
  }

  confirmLockPeriod(): void {
    const period = this.periodToLock();
    if (!period) return;

    this.isLocking.set(true);
    this.lockError.set(null);

    const request: FinalizePeriodRequest = {
      periodId: period.id,
      signatureToken: this.lockSignatureToken().trim() || null,
      approvalComments: this.lockApprovalComments().trim() || null,
    };

    this.evaluationService
      .finalize(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isLocking.set(false);
          this.closeLockModal();
          this.showToast(this.localization.evaluations.branchPeriods.lockSuccess);
          this.loadData();
        },
        error: (error: unknown) => {
          this.isLocking.set(false);
          const errorCode = error instanceof HttpErrorResponse ? error.error?.errorCode : null;
          const text = this.localization.evaluations.branchPeriods;
          this.lockError.set(
            errorCode === 'EVALUATION_DISPUTE_PENDING' ? text.lockDisputePending : text.lockError,
          );
        },
      });
  }

  private showToast(msg: string): void {
    clearTimeout(this.toastTimer);
    this.toastMessage.set(msg);
    this.toastTimer = setTimeout(() => {
      if (this.toastMessage() === msg) {
        this.toastMessage.set(null);
      }
    }, TOAST_DURATION_MS);
  }
}
