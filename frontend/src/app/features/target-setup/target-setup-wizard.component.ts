import { Component, computed, DestroyRef, inject, OnInit, signal, ViewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { switchMap, throwError } from 'rxjs';
import { ApiResponse } from '../../core/api-response.model';
import { DepartmentSummary } from '../../core/department.models';
import { DepartmentService } from '../../core/department.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { TargetSetup, TargetSetupStep } from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { ImportBranchTargetModalComponent } from '../target-overview/import-branch-target-modal/import-branch-target-modal.component';
import { fetchAllPages } from '../target-overview/target-overview.utils';
import { BranchQuarterSplitComponent } from './branch-quarter-split/branch-quarter-split.component';
import { DepartmentAllocationComponent } from './department-allocation/department-allocation.component';
import { DepartmentAllocationMatrixComponent } from './department-allocation-matrix/department-allocation-matrix.component';
import { PublishStepComponent } from './publish-step/publish-step.component';

type WizardStepNumber = 1 | 2 | 3 | 4;

/** Step 3 view switch (plan 04-D1 v2): "Theo chỉ tiêu" (per-KPI, slice C) vs "Ma trận" (slice D). */
export type AllocationViewMode = 'kpi' | 'matrix';

/** `GET /setup` step to the wizard step it belongs to (docs/api/TARGET_ALLOCATION_CONTRACT.md, 2.A). */
const STEP_OF: Record<TargetSetupStep, WizardStepNumber> = {
  IMPORT: 1,
  QUARTER: 2,
  ALLOCATE: 3,
  PUBLISH: 4,
  PUBLISHED: 4,
};

interface WizardStepView {
  number: WizardStepNumber;
  label: string;
  done: boolean;
  current: boolean;
  clickable: boolean;
}

/**
 * SCR-STRAT-04: target allocation wizard shell. Reads the year from the route, resolves the branch the same way
 * `BranchTargetOverviewComponent` does, and shows the four steps: import (modal), quarter split, department
 * allocation and the publish confirmation.
 */
/** Sentinel for a signed-in user whose department scope holds no branch (e.g. a department manager). */
const NO_BRANCH = 'NO_BRANCH';

@Component({
  selector: 'app-target-setup-wizard',
  imports: [
    HeaderComponent,
    SidebarComponent,
    BranchQuarterSplitComponent,
    DepartmentAllocationComponent,
    DepartmentAllocationMatrixComponent,
    PublishStepComponent,
    ImportBranchTargetModalComponent,
  ],
  templateUrl: './target-setup-wizard.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/target/target-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './target-setup-wizard.component.scss',
  ],
})
export class TargetSetupWizardComponent extends LocalizedComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly departmentApi = inject(DepartmentService);
  private readonly targetApi = inject(TargetAssignmentService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly noAccess = signal(false);
  protected readonly year = signal<number | null>(null);
  protected readonly setup = signal<TargetSetup | null>(null);
  protected readonly branchId = signal<string | null>(null);
  /** Every department (W7 reads each one's manager from it). */
  protected readonly departments = signal<readonly DepartmentSummary[]>([]);
  protected readonly showImportModal = signal(false);
  protected readonly confirmingPublish = signal(false);
  protected readonly publishing = signal(false);
  protected readonly publishError = signal<string | null>(null);
  /** KPI to open in step 3 after `Phân bổ ›` was pressed in step 2. */
  protected readonly allocationKpiId = signal<string | null>(null);
  /** Step 3 "Theo chỉ tiêu | Ma trận" toggle (kept here so it survives switching away from step 3). */
  protected readonly allocationView = signal<AllocationViewMode>('kpi');
  /** The view switch tab clicked while the current view had unsaved edits, awaiting the confirm dialog. */
  protected readonly confirmingViewSwitch = signal<AllocationViewMode | null>(null);
  @ViewChild(DepartmentAllocationComponent) private kpiView?: DepartmentAllocationComponent;
  @ViewChild(DepartmentAllocationMatrixComponent)
  private matrixView?: DepartmentAllocationMatrixComponent;
  @ViewChild(BranchQuarterSplitComponent) private quarterView?: BranchQuarterSplitComponent;
  /** `?step=2&suggest=1` from the overview's "✦ Gợi ý chia quý": run the suggestion once step 2 opens. */
  protected readonly suggestOnOpen = signal(false);
  /** Step number the user clicked while step 2 had an unapplied ✦ suggestion, awaiting the confirm dialog. */
  protected readonly confirmingLeaveSuggestion = signal<WizardStepNumber | null>(null);
  private readonly selected = signal<WizardStepNumber | null>(null);
  private appliedQueryStep = false;

  protected readonly title = computed(() =>
    this.localization.targetSetup.title.replace('{year}', `${this.year() ?? ''}`),
  );
  protected readonly currentStep = computed<WizardStepNumber>(() => {
    const step = this.setup()?.step;
    return step ? (STEP_OF[step] ?? 1) : 1;
  });
  /** A published branch year is complete, so every step renders as done. */
  private readonly allDone = computed(() => this.setup()?.step === 'PUBLISHED');
  protected readonly steps = computed<WizardStepView[]>(() => {
    const text = this.localization.targetSetup;
    const labels = [text.stepImport, text.stepQuarter, text.stepAllocate, text.stepPublish];
    const current = this.currentStep();
    return labels.map((label, index) => {
      const number = (index + 1) as WizardStepNumber;
      const done = this.allDone() || number < current;
      return {
        number,
        label,
        done,
        current: !done && number === current,
        clickable: done || number === current,
      };
    });
  });
  protected readonly activeStep = computed<WizardStepNumber>(
    () => this.selected() ?? this.currentStep(),
  );
  protected readonly activeTitle = computed(
    () => this.steps().find((step) => step.number === this.activeStep())?.label ?? '',
  );

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const year = Number(params.get('year'));
      this.year.set(Number.isInteger(year) && year > 0 ? year : null);
      this.selected.set(null);
      this.appliedQueryStep = false;
      this.load();
    });
  }

  /**
   * Applies `?step=2&suggest=1` exactly once per navigation, right after the setup loads: opens step 2 (only
   * while the year is still DRAFT, i.e. step 2 is clickable) and asks `app-branch-quarter-split` to suggest a
   * split. Then strips the query params so a reload of the page doesn't re-apply it.
   */
  private applyQueryStepOnce(): void {
    if (this.appliedQueryStep) {
      return;
    }
    this.appliedQueryStep = true;

    const params = this.route.snapshot.queryParamMap;
    const step = params.get('step');
    const suggest = params.get('suggest');
    if (!step && !suggest) {
      return;
    }

    if (step === '2' && this.setup()?.status === 'DRAFT') {
      this.selected.set(2);
      this.suggestOnOpen.set(suggest === '1');
    }

    const year = this.year();
    if (year !== null) {
      void this.router.navigate(['/target-setup', year], { replaceUrl: true });
    }
  }

  protected selectStep(step: WizardStepView): void {
    if (!step.clickable) {
      return;
    }
    if (this.activeStep() === 2 && step.number !== 2 && this.quarterView?.isSuggestionPending()) {
      this.confirmingLeaveSuggestion.set(step.number);
      return;
    }
    this.selected.set(step.number);
  }

  protected cancelLeaveSuggestion(): void {
    this.confirmingLeaveSuggestion.set(null);
  }

  protected confirmLeaveSuggestion(): void {
    const step = this.confirmingLeaveSuggestion();
    if (step) {
      this.selected.set(step);
    }
    this.confirmingLeaveSuggestion.set(null);
  }

  /** Step 2 `Phân bổ ›`: opens step 3 on that KPI. */
  protected onAllocateKpi(kpiId: string): void {
    this.allocationKpiId.set(kpiId);
    if (this.quarterView?.isSuggestionPending()) {
      this.confirmingLeaveSuggestion.set(3);
      return;
    }
    this.selected.set(3);
  }

  /** Step 3 view switch: asks a confirm when the current view has unsaved edits, otherwise switches right away. */
  protected setAllocationView(view: AllocationViewMode): void {
    if (view === this.allocationView()) {
      return;
    }
    const current = this.allocationView() === 'kpi' ? this.kpiView : this.matrixView;
    if (current?.isDirty()) {
      this.confirmingViewSwitch.set(view);
      return;
    }
    this.allocationView.set(view);
  }

  protected cancelViewSwitch(): void {
    this.confirmingViewSwitch.set(null);
  }

  protected confirmViewSwitch(): void {
    const view = this.confirmingViewSwitch();
    if (view) {
      this.allocationView.set(view);
    }
    this.confirmingViewSwitch.set(null);
  }

  /** W6 column header: opens that KPI in the "Theo chỉ tiêu" view (asks first when the matrix has unsaved edits). */
  protected openKpiView(kpiId: string): void {
    this.allocationKpiId.set(kpiId);
    this.setAllocationView('kpi');
  }

  protected goToStep(step: WizardStepNumber): void {
    this.selected.set(step);
  }

  protected askPublish(): void {
    if (this.setup()?.canPublish) {
      this.publishError.set(null);
      this.confirmingPublish.set(true);
    }
  }

  protected cancelPublish(): void {
    this.confirmingPublish.set(false);
  }

  /** Step 4: activates every draft target of the branch year, then returns to the overview. */
  protected confirmPublish(): void {
    const branchYearId = this.setup()?.branchYearId;
    if (!branchYearId || this.publishing()) {
      return;
    }
    this.publishing.set(true);
    this.publishError.set(null);
    this.targetApi
      .publish(branchYearId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.publishing.set(false);
          this.confirmingPublish.set(false);
          void this.router.navigate(['/target-overview']);
        },
        error: (error: HttpErrorResponse) => {
          this.publishing.set(false);
          this.confirmingPublish.set(false);
          const code = (error.error as ApiResponse<unknown> | undefined)?.errorCode;
          const text = this.localization.targetSetup;
          this.publishError.set(
            code === 'TARGET_PUBLISH_BLOCKED'
              ? text.publishBlocked
              : code === 'TARGET_INVALID_STATE'
                ? text.publishInvalidState
                : text.publishFailed,
          );
          this.refreshSetup();
        },
      });
  }

  /** Re-reads the setup after a save without the full-page loading state (keeps the open step mounted). */
  protected refreshSetup(): void {
    const branchId = this.branchId();
    const year = this.year();
    if (!branchId || year === null) {
      return;
    }
    this.targetApi
      .getSetup(branchId, year)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (setup) => this.setup.set(setup), error: () => undefined });
  }

  protected openReimport(): void {
    if (this.branchId() && this.year() !== null) {
      this.showImportModal.set(true);
    }
  }

  protected closeReimport(): void {
    this.showImportModal.set(false);
  }

  protected onReimportCommitted(): void {
    this.showImportModal.set(false);
    this.load();
  }

  protected load(): void {
    const year = this.year();
    if (year === null) {
      this.failed.set(true);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.failed.set(false);
    this.noAccess.set(false);
    fetchAllPages((page, pageSize) => this.departmentApi.list({ page, pageSize }))
      .pipe(
        switchMap((departments) => {
          this.departments.set(departments);
          const branchId = departments.find((d) => d.levelType === 'BRANCH')?.id ?? null;
          this.branchId.set(branchId);
          return branchId
            ? this.targetApi.getSetup(branchId, year)
            : throwError(() => new Error(NO_BRANCH));
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (setup) => {
          this.setup.set(setup);
          this.loading.set(false);
          this.applyQueryStepOnce();
        },
        error: (error: unknown) => {
          this.noAccess.set(
            (error instanceof Error && error.message === NO_BRANCH) ||
              (error instanceof HttpErrorResponse &&
                (error.status === 403 || error.status === 404)),
          );
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }
}
