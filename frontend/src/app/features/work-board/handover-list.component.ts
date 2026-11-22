import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  HostListener,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subject, catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { DepartmentMember } from '../../core/department.models';
import {
  HandoverDirection,
  HandoverKpiStatus,
  HandoverListItem,
  HandoverStatus,
} from '../../core/handover.models';
import { HandoverService } from '../../core/handover.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  SelectOption,
  StatusBadgeComponent,
  StatusBadgeTone,
  TablePaginationComponent,
  UiSearchInputComponent,
  UiSelectComponent,
} from '../../shared/ui';

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

/**
 * SCR-WRK-05 & SCR-WRK-05-D1: incoming / outgoing transferred works list with a detail drawer. The recipient only reads;
 * the requester's direct manager approves, reassigns or rejects a pending request there.
 */
@Component({
  selector: 'app-handover-list',
  standalone: true,
  imports: [
    DatePipe,
    FormsModule,
    StatusBadgeComponent,
    TablePaginationComponent,
    UiSearchInputComponent,
    UiSelectComponent,
  ],
  templateUrl: './handover-list.component.html',
  styleUrl: './handover-list.component.scss',
})
export class HandoverListComponent extends LocalizedComponent {
  private readonly handovers = inject(HandoverService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly search$ = new Subject<string>();
  private readonly loadTrigger$ = new Subject<void>();

  readonly members = input<readonly DepartmentMember[]>([]);
  readonly initialDirection = input<HandoverDirection>('INCOMING');
  readonly openWork = output<string>();
  readonly changed = output<{ kind: 'success' | 'error'; text: string }>();

  protected readonly direction = signal<HandoverDirection>('INCOMING');
  protected readonly statusFilter = signal<HandoverStatus | ''>('');
  protected readonly kpiFilter = signal<HandoverKpiStatus | ''>('');
  protected readonly senderFilter = signal('');
  protected readonly keyword = signal('');
  protected readonly page = signal(1);

  protected readonly items = signal<HandoverListItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly selected = signal<HandoverListItem | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailFailed = signal(false);
  protected readonly acting = signal(false);
  protected readonly reassigning = signal(false);
  protected readonly newRecipient = signal('');

  protected readonly pageSize = PAGE_SIZE;

  protected readonly statusOptions = computed<SelectOption<HandoverStatus | ''>[]>(() => {
    const t = this.localization.work.handoverList;
    return [
      { value: '', label: t.filterStatusAll },
      { value: 'ACCEPTED', label: t.statusAccepted },
      { value: 'PENDING', label: t.statusPending },
      { value: 'REJECTED', label: t.statusRejected },
    ];
  });

  protected readonly kpiOptions = computed<SelectOption<HandoverKpiStatus | ''>[]>(() => {
    const t = this.localization.work.handoverList;
    return [
      { value: '', label: t.filterKpiAll },
      { value: 'PENDING', label: t.kpiPending },
      { value: 'RECORDED', label: t.kpiRecorded },
    ];
  });

  protected readonly senderOptions = computed<SelectOption<string>[]>(() => {
    const t = this.localization.work.handoverList;
    const seen = new Map<string, string>();
    for (const m of this.members()) {
      seen.set(m.id, `${m.fullName} (${m.employeeCode})`);
    }
    for (const item of this.items()) {
      if (!seen.has(item.fromEmployeeId)) {
        seen.set(item.fromEmployeeId, `${item.fromEmployeeName} (${item.fromEmployeeCode})`);
      }
    }
    return [
      { value: '', label: t.filterSenderAll },
      ...[...seen.entries()]
        .sort((a, b) => a[1].localeCompare(b[1]))
        .map(([value, label]) => ({ value, label })),
    ];
  });

  /** Colleagues the manager can give the request to instead: active, and neither the giver nor the current recipient. */
  protected readonly reassignOptions = computed<SelectOption<string>[]>(() => {
    const item = this.selected();
    const t = this.localization.work.handoverList;
    const options = this.members()
      .filter(
        (m) =>
          m.status === 'ACTIVE' && m.id !== item?.fromEmployeeId && m.id !== item?.toEmployeeId,
      )
      .map((m) => ({ value: m.id, label: `${m.fullName} (${m.employeeCode})` }));
    return [{ value: '', label: t.reassignPlaceholder }, ...options];
  });

  protected readonly anyReadOnly = computed(
    () => this.items().length > 0 && this.items().every((item) => item.isReadOnly),
  );

  constructor() {
    super();

    effect(() => {
      const dir = this.initialDirection();
      untracked(() => {
        if (dir !== this.direction()) {
          this.direction.set(dir);
          this.page.set(1);
          this.loadTrigger$.next();
        }
      });
    });

    this.search$
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((term) => {
        this.keyword.set(term);
        this.page.set(1);
        this.loadTrigger$.next();
      });

    this.loadTrigger$
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          return this.handovers
            .list({
              direction: this.direction(),
              status: this.statusFilter(),
              kpiStatus: this.kpiFilter(),
              fromEmployeeId: this.senderFilter() || undefined,
              keyword: this.keyword(),
              page: this.page(),
              pageSize: PAGE_SIZE,
            })
            .pipe(
              catchError(() => {
                this.failed.set(true);
                this.loading.set(false);
                return of(null);
              }),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        if (!result) return;
        this.items.set(result.items);
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      });

    this.loadTrigger$.next();
  }

  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    const current = this.selected();
    if (!current) return;
    // Keys typed in the recipient picker belong to the picker (Esc closes its menu, arrows move in it).
    if (this.reassigning() && (event.target as HTMLElement | null)?.closest('.reassign-row'))
      return;
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closeDrawer();
      return;
    }
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
    const rows = this.items();
    const index = rows.findIndex((row) => row.id === current.id);
    if (index < 0) return;
    const nextIndex = event.key === 'ArrowDown' ? index + 1 : index - 1;
    if (nextIndex >= 0 && nextIndex < rows.length) {
      event.preventDefault();
      this.openDetail(rows[nextIndex]);
    }
  }

  protected setDirection(dir: HandoverDirection): void {
    if (this.direction() === dir) return;
    this.direction.set(dir);
    this.page.set(1);
    this.selected.set(null);
    this.loadTrigger$.next();
  }

  protected onSearchChange(term: string): void {
    this.search$.next(term);
  }

  protected onStatusFilterChange(value: HandoverStatus | ''): void {
    this.statusFilter.set(value);
    this.page.set(1);
    this.loadTrigger$.next();
  }

  protected onKpiFilterChange(value: HandoverKpiStatus | ''): void {
    this.kpiFilter.set(value);
    this.page.set(1);
    this.loadTrigger$.next();
  }

  protected onSenderFilterChange(value: string): void {
    this.senderFilter.set(value);
    this.page.set(1);
    this.loadTrigger$.next();
  }

  protected onPageChange(nextPage: number): void {
    this.page.set(nextPage);
    this.loadTrigger$.next();
  }

  protected retry(): void {
    this.loadTrigger$.next();
  }

  protected openDetail(item: HandoverListItem): void {
    this.selected.set(item);
    this.detailLoading.set(true);
    this.detailFailed.set(false);
    this.handovers
      .detail(item.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (fresh) => {
          this.selected.set(fresh);
          this.detailLoading.set(false);
        },
        error: () => {
          this.detailFailed.set(true);
          this.detailLoading.set(false);
        },
      });
  }

  protected closeDrawer(): void {
    if (this.acting()) return;
    this.selected.set(null);
    this.detailFailed.set(false);
    this.reassigning.set(false);
    this.newRecipient.set('');
  }

  protected openRelatedWork(workId: string): void {
    this.openWork.emit(workId);
  }

  protected canDecide(item: HandoverListItem): boolean {
    return !item.isReadOnly && item.status === 'PENDING';
  }

  protected startReassign(): void {
    this.newRecipient.set('');
    this.reassigning.set(true);
  }

  protected cancelReassign(): void {
    this.reassigning.set(false);
    this.newRecipient.set('');
  }

  protected decide(action: 'approve' | 'reassign' | 'reject'): void {
    const item = this.selected();
    if (!item || !this.canDecide(item) || this.acting()) return;
    const recipient = this.newRecipient();
    if (action === 'reassign' && !recipient) return;

    this.acting.set(true);
    const call$ =
      action === 'approve'
        ? this.handovers.approve(item.id)
        : action === 'reassign'
          ? this.handovers.reassign(item.id, recipient)
          : this.handovers.rejectAsManager(item.id);
    call$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.acting.set(false);
        this.changed.emit({
          kind: 'success',
          text: this.i18n.translate(`work.handoverList.${action}Success`, {
            code: item.handoverCode,
          }),
        });
        this.closeDrawer();
        this.loadTrigger$.next();
      },
      error: (error: HttpErrorResponse) => {
        this.acting.set(false);
        this.changed.emit({ kind: 'error', text: this.decideErrorText(error) });
        if (error.status === 409) this.loadTrigger$.next();
      },
    });
  }

  private decideErrorText(error: HttpErrorResponse): string {
    const t = this.localization.work.handoverList;
    const code: unknown = error.error?.errorCode;
    if (code === 'HANDOVER_NOT_PENDING') return t.alreadyDecided;
    if (code === 'WORK_INVALID_STATE') return t.workInvalidState;
    if (error.status === 403) return t.forbidden;
    return t.actionError;
  }
  protected statusLabel(status: HandoverStatus): string {
    const t = this.localization.work.handoverList;
    switch (status) {
      case 'ACCEPTED':
        return t.statusAccepted;
      case 'REJECTED':
        return t.statusRejected;
      default:
        return t.statusPending;
    }
  }

  protected statusTone(status: HandoverStatus): StatusBadgeTone {
    switch (status) {
      case 'ACCEPTED':
        return 'success';
      case 'REJECTED':
        return 'danger';
      default:
        return 'warning';
    }
  }

  protected kpiLabel(status: HandoverKpiStatus): string {
    const t = this.localization.work.handoverList;
    return status === 'RECORDED' ? t.kpiRecorded : t.kpiPending;
  }

  protected kpiTone(status: HandoverKpiStatus): StatusBadgeTone {
    return status === 'RECORDED' ? 'success' : 'neutral';
  }
}
