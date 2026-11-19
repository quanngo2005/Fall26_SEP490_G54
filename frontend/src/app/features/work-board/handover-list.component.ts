import { DatePipe } from '@angular/common';
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

/** SCR-WRK-05 & SCR-WRK-05-D1: Incoming / outgoing transferred works list with read-only detail drawer. */
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
  }

  protected openRelatedWork(workId: string): void {
    this.openWork.emit(workId);
  }

  protected decide(action: 'accept' | 'reject'): void {
    const item = this.selected();
    if (!item || item.isReadOnly || item.status !== 'PENDING' || this.acting()) return;
    this.acting.set(true);
    const call$ =
      action === 'accept' ? this.handovers.accept(item.id) : this.handovers.reject(item.id);
    call$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.acting.set(false);
        const key =
          action === 'accept'
            ? 'work.handoverList.acceptSuccess'
            : 'work.handoverList.rejectSuccess';
        this.changed.emit({
          kind: 'success',
          text: this.i18n.translate(key, { code: item.handoverCode }),
        });
        this.selected.set(null);
        this.loadTrigger$.next();
      },
      error: () => {
        this.acting.set(false);
        this.changed.emit({
          kind: 'error',
          text: this.localization.work.handoverList.actionError,
        });
      },
    });
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
