import { Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { WorkTypeService } from '../../core/work-type.service';
import { WorkTypeDetail, WorkTypeStatus } from '../../core/work-type.models';
import { WorkService } from '../../core/work.service';
import { COMPLETED_WORK_STATUSES, OPEN_WORK_STATUSES, WorkStatus } from '../../core/work.models';

interface WorkUsage {
  open: number;
  completed: number;
}

/** SCR-CFG-02-D1: detail drawer of the work type selected in the SCR-CFG-02 list. */
@Component({
  selector: 'app-work-type-detail-drawer',
  standalone: true,
  templateUrl: './work-type-detail-drawer.component.html',
  styleUrl: './work-type-detail-drawer.component.scss',
})
export class WorkTypeDetailDrawerComponent extends LocalizedComponent {
  private readonly workTypes = inject(WorkTypeService);
  private readonly works = inject(WorkService);

  readonly workType = input.required<WorkTypeDetail>();
  readonly positionName = input<string | null>(null);
  readonly resultUnit = input<string | null>(null);
  readonly transactionCif = input<string | null>(null);
  readonly closeDrawer = output<void>();
  /** Emitted after a successful status change so the list can reload. */
  readonly changed = output<void>();

  /** null while loading or when the work list could not be read; never a made-up number. */
  protected readonly usage = signal<WorkUsage | null>(null);
  protected readonly confirmingStatus = signal(false);
  protected readonly saving = signal(false);
  protected readonly actionFailed = signal(false);

  constructor() {
    super();
    toObservable(this.workType)
      .pipe(
        switchMap((workType) => {
          this.usage.set(null);
          this.confirmingStatus.set(false);
          this.actionFailed.set(false);
          return forkJoin([
            this.count(workType.id, OPEN_WORK_STATUSES),
            this.count(workType.id, COMPLETED_WORK_STATUSES),
          ]).pipe(
            map(([open, completed]): WorkUsage => ({ open, completed })),
            catchError(() => of(null)),
          );
        }),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((usage) => this.usage.set(usage));
  }

  protected statusLabel(status: WorkTypeStatus): string {
    const t = this.localization.workTypes;
    return status === 'ACTIVE'
      ? t.statusActive
      : status === 'INACTIVE'
        ? t.statusInactive
        : t.statusArchived;
  }

  protected confirmStatusChange(): void {
    const next: WorkTypeStatus = this.workType().status === 'ACTIVE' ? 'INACTIVE' : 'ACTIVE';
    this.saving.set(true);
    this.actionFailed.set(false);
    this.workTypes.changeStatus(this.workType().id, next).subscribe({
      next: () => {
        this.saving.set(false);
        this.confirmingStatus.set(false);
        this.changed.emit();
      },
      error: () => {
        this.saving.set(false);
        this.actionFailed.set(true);
      },
    });
  }

  private count(workTypeId: string, status: readonly WorkStatus[]) {
    return this.works
      .list({ workTypeId, status, page: 1, pageSize: 1 })
      .pipe(map((result) => result.totalCount));
  }
}
