import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { switchMap, throwError } from 'rxjs';
import { ApiResponse } from '../../core/api-response.model';
import { DepartmentService } from '../../core/department.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { TargetSetup, TargetSetupStep } from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { ImportBranchTargetModalComponent } from '../target-overview/import-branch-target-modal/import-branch-target-modal.component';
import { fetchAllPages } from '../target-overview/target-overview.utils';
import { BranchQuarterSplitComponent } from './branch-quarter-split/branch-quarter-split.component';
import { DepartmentAllocationComponent } from './department-allocation/department-allocation.component';

type WizardStepNumber = 1 | 2 | 3 | 4;

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
@Component({
  selector: 'app-target-setup-wizard',
  imports: [
    HeaderComponent,
    SidebarComponent,
    BranchQuarterSplitComponent,
    DepartmentAllocationComponent,
    ImportBranchTargetModalComponent,
  ],
  templateUrl: './target-setup-wizard.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
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
  protected readonly year = signal<number | null>(null);
  protected readonly setup = signal<TargetSetup | null>(null);
  protected readonly branchId = signal<string | null>(null);
  protected readonly showImportModal = signal(false);
  protected readonly confirmingPublish = signal(false);
  protected readonly publishing = signal(false);
  protected readonly publishError = signal<string | null>(null);
  /** KPI to open in step 3 after `Phân bổ ›` was pressed in step 2. */
  protected readonly allocationKpiId = signal<string | null>(null);
  private readonly selected = signal<WizardStepNumber | null>(null);

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
      this.load();
    });
  }

  protected selectStep(step: WizardStepView): void {
    if (step.clickable) {
      this.selected.set(step.number);
    }
  }

  /** Step 2 `Phân bổ ›`: opens step 3 on that KPI. */
  protected onAllocateKpi(kpiId: string): void {
    this.allocationKpiId.set(kpiId);
    this.selected.set(3);
  }

  protected goToStep(step: WizardStepNumber): void {
    this.selected.set(step);
  }

  protected summaryText(): string {
    const setup = this.setup();
    const text = this.localization.targetSetup;
    if (!setup) {
      return '';
    }
    return text.publishSummary
      .replace('{quarterDone}', `${setup.quarters.done}`)
      .replace('{quarterTotal}', `${setup.quarters.total}`)
      .replace('{allocationDone}', `${setup.allocation.done}`)
      .replace('{allocationTotal}', `${setup.allocation.total}`);
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
    fetchAllPages((page, pageSize) => this.departmentApi.list({ page, pageSize }))
      .pipe(
        switchMap((departments) => {
          const branchId = departments.find((d) => d.levelType === 'BRANCH')?.id ?? null;
          this.branchId.set(branchId);
          return branchId
            ? this.targetApi.getSetup(branchId, year)
            : throwError(() => new Error('No branch department'));
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (setup) => {
          this.setup.set(setup);
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }
}
