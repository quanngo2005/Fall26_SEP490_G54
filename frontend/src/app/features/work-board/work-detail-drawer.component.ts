import {
  Component,
  DestroyRef,
  HostListener,
  NgZone,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, map, of, switchMap, tap } from 'rxjs';
import { EmployeeService } from '../../core/employee.service';
import { HandoverDetail } from '../../core/handover.models';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { MySubmissionDetail } from '../../core/my-submission.models';
import { MySubmissionService } from '../../core/my-submission.service';
import { SubmissionDetail } from '../../core/result-submission.models';
import { ResultSubmissionService } from '../../core/result-submission.service';
import { WorkActivity, WorkChecklistItem, WorkDetail, WorkListItem } from '../../core/work.models';
import { WorkService } from '../../core/work.service';
import { OPEN_FOR_ASSIGNEE, deadlineSpan, spanParts } from '../work-detail/work-detail.utils';
import { WorkPlanDialogComponent } from '../work-detail/work-plan-dialog.component';
import { HandoverDialogComponent } from './handover-dialog.component';
import { dateOnly } from './work-board.utils';

type LoadState = 'loading' | 'ready' | 'error';
type Tone = 'green' | 'red' | 'gray';

interface SubmissionRow {
  id: string;
  versionNo: number;
  submittedAt: string;
  codes: string;
  label: string;
  tone: Tone;
}

interface Notice {
  kind: 'success' | 'error';
  text: string;
}

const CLOCK_TICK_MS = 30_000;
const NOTICE_MS = 5000;
/** Works the assignee can still hand over (same rule as the board). */
const HANDOVER_STATUSES: readonly string[] = ['New', 'InProgress'];

/**
 * SCR-WRK-03 / SCR-WRK-03-M1: everything about one work in the side panel of the board (`GET /works/{id}`).
 * The assignee sees the checklist they can tick and the actions they have (hand over, submit); the assignee's manager
 * sees the same work with "Sửa kế hoạch". Which of the two it is comes from the server (`canEditPlan`,
 * `canUpdateChecklist`) and from who the signed-in employee is. `changed` tells the board to refresh its cards.
 */
@Component({
  selector: 'app-work-detail-drawer',
  standalone: true,
  imports: [HandoverDialogComponent, WorkPlanDialogComponent],
  templateUrl: './work-detail-drawer.component.html',
  styleUrls: ['./work-detail-drawer.component.scss', './work-detail-drawer.component.part2.scss'],
})
export class WorkDetailDrawerComponent extends LocalizedComponent {
  private readonly works = inject(WorkService);
  private readonly submissionApi = inject(ResultSubmissionService);
  private readonly mySubmissions = inject(MySubmissionService);
  private readonly employees = inject(EmployeeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly zone = inject(NgZone);
  private noticeTimer: ReturnType<typeof setTimeout> | undefined;

  readonly workId = input.required<string>();
  readonly closed = output<void>();
  /** The work was changed here (checklist, plan, hand-over), so the board's cards may be out of date. */
  readonly changed = output<void>();

  protected readonly work = signal<WorkDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly activities = signal<WorkActivity[]>([]);
  protected readonly activitiesState = signal<LoadState>('loading');
  protected readonly submissions = signal<SubmissionRow[]>([]);
  protected readonly submissionsState = signal<LoadState>('loading');
  protected readonly isAssignee = signal(false);
  protected readonly planOpen = signal(false);
  protected readonly handoverOpen = signal(false);
  protected readonly savingItem = signal<string | null>(null);
  protected readonly notice = signal<Notice | null>(null);
  private readonly now = signal(new Date());

  protected readonly teamView = computed(() => !this.isAssignee());
  protected readonly checklist = computed<WorkChecklistItem[]>(() =>
    [...(this.work()?.checklistItems ?? [])].sort((a, b) => a.sortOrder - b.sortOrder),
  );
  protected readonly doneCount = computed(() => this.checklist().filter((i) => i.isDone).length);
  protected readonly progress = computed(() => this.work()?.progressPercent ?? 0);
  protected readonly canHandover = computed(() => {
    const work = this.work();
    return !!work && this.isAssignee() && HANDOVER_STATUSES.includes(work.status);
  });
  protected readonly canSubmit = computed(() => {
    const work = this.work();
    return !!work && this.isAssignee() && OPEN_FOR_ASSIGNEE.includes(work.status);
  });
  /** The one work the hand-over dialog can pick, shaped like a board card. */
  protected readonly handoverWorks = computed<WorkListItem[]>(() => {
    const work = this.work();
    return work ? [this.toListItem(work)] : [];
  });

  constructor() {
    super();
    // Outside Angular's zone: a repeating timer inside it would keep the app from ever being "stable".
    const timer = this.zone.runOutsideAngular(() =>
      setInterval(() => this.zone.run(() => this.now.set(new Date())), CLOCK_TICK_MS),
    );
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      clearTimeout(this.noticeTimer);
    });

    toObservable(this.workId)
      .pipe(
        tap(() => this.reset()),
        switchMap((id) =>
          this.works.detail(id).pipe(
            map((work) => ({ work })),
            catchError(() => of({ work: null })),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ work }) => {
        this.loading.set(false);
        if (!work) {
          this.failed.set(true);
          return;
        }
        this.work.set(work);
        this.loadSideData(work);
      });
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    // An open dialog takes the key first; the panel stays until it is closed.
    if (this.planOpen() || this.handoverOpen()) return;
    this.closed.emit();
  }

  protected reload(): void {
    this.reset();
    this.works
      .detail(this.workId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (work) => {
          this.loading.set(false);
          this.work.set(work);
          this.loadSideData(work);
        },
        error: () => {
          this.loading.set(false);
          this.failed.set(true);
        },
      });
  }

  // ---- text -----------------------------------------------------------------------------------------

  protected day(value: string | null): string {
    return value
      ? this.i18n.formatDate(dateOnly(value))
      : this.localization.department.notAvailable;
  }

  protected orNone(value: string | null | undefined): string {
    return value?.trim() ? value : this.localization.work.detail.request.notProvided;
  }

  protected when(value: string | null | undefined): string {
    return value ? this.i18n.formatDate(value, 'DD/MM/YYYY HH:mm') : this.orNone(null);
  }

  protected deadlineText(work: WorkDetail): string {
    return work.plannedEndDate
      ? this.i18n.translate('work.detail.deadline', {
          date: this.i18n.formatDate(dateOnly(work.plannedEndDate)),
        })
      : this.localization.work.detail.noDeadline;
  }

  /** "Còn 1 ngày 6 giờ" / "Đã trễ 2 giờ"; nothing once the work no longer waits on its deadline. */
  protected timeText(work: WorkDetail): string | null {
    if (!OPEN_FOR_ASSIGNEE.includes(work.status) && work.status !== 'HandedOver') return null;
    const span = deadlineSpan(work.plannedEndDate, this.now());
    if (!span) return null;
    const parts = spanParts(span);
    if (parts.length === 0) return this.localization.work.detail.dueToday;
    const time = parts
      .map((part) => this.i18n.translate(`work.detail.units.${part.unit}`, { n: part.n }))
      .join(' ');
    return this.i18n.translate(span.overdue ? 'work.detail.timeOver' : 'work.detail.timeLeft', {
      time,
    });
  }

  protected isLate(work: WorkDetail): boolean {
    const span = deadlineSpan(work.plannedEndDate, this.now());
    return !!span && span.overdue && OPEN_FOR_ASSIGNEE.includes(work.status);
  }

  protected byLine(work: WorkDetail): string {
    const request = this.localization.work.detail.request;
    if (this.teamView()) {
      return this.i18n.translate('work.detail.assigneeLine', {
        name: work.assigneeEmployeeName ?? request.notProvided,
      });
    }
    const name = work.assignedByName ?? request.notProvided;
    return work.assignedByPositionTitle
      ? this.i18n.translate('work.detail.assignedByRole', {
          name,
          role: work.assignedByPositionTitle,
        })
      : this.i18n.translate('work.detail.assignedBy', { name });
  }

  protected assignedTag(work: WorkDetail): string {
    return work.assignedByPositionTitle
      ? this.i18n.translate('work.detail.request.assignedTag', {
          role: work.assignedByPositionTitle,
        })
      : this.localization.work.detail.request.assignedTagFallback;
  }

  protected actor(activity: WorkActivity): string {
    return activity.actorName ?? this.localization.work.detail.activity.system;
  }

  // ---- checklist, plan, hand-over -------------------------------------------------------------------

  protected tick(item: WorkChecklistItem, event: Event): void {
    const work = this.work();
    const box = event.target as HTMLInputElement;
    if (!work || !work.canUpdateChecklist || this.savingItem()) {
      box.checked = item.isDone;
      return;
    }

    this.savingItem.set(item.id);
    this.works
      .setChecklistItem(work.id, item.id, box.checked)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.savingItem.set(null);
          this.work.set(updated);
          this.loadActivities(updated.id);
          this.changed.emit();
        },
        error: () => {
          this.savingItem.set(null);
          box.checked = item.isDone;
          this.showNotice('error', this.localization.work.detail.progress.tickError);
        },
      });
  }

  protected onPlanSaved(updated: WorkDetail): void {
    this.planOpen.set(false);
    this.work.set(updated);
    this.loadActivities(updated.id);
    this.showNotice('success', this.localization.work.detail.toast.planSaved);
    this.changed.emit();
  }

  protected onHandoverRequested(handover: HandoverDetail): void {
    this.handoverOpen.set(false);
    this.showNotice(
      'success',
      this.i18n.translate('work.handover.success', {
        code: handover.workCode,
        name: handover.toEmployeeName,
      }),
    );
    this.loadActivities(this.workId());
    this.changed.emit();
  }

  protected dismissNotice(): void {
    this.notice.set(null);
  }

  // ---- loading --------------------------------------------------------------------------------------

  private reset(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.work.set(null);
    this.activities.set([]);
    this.activitiesState.set('loading');
    this.submissions.set([]);
    this.submissionsState.set('loading');
    this.isAssignee.set(false);
    this.planOpen.set(false);
    this.handoverOpen.set(false);
    this.notice.set(null);
  }

  private loadSideData(work: WorkDetail): void {
    this.loadActivities(work.id);
    this.employees
      .me()
      .pipe(
        map((me) => me.id === work.assigneeEmployeeId),
        catchError(() => of(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((isAssignee) => {
        this.isAssignee.set(isAssignee);
        this.loadSubmissions(work, isAssignee);
      });
  }

  private loadActivities(id: string): void {
    this.works
      .activities(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.activities.set(list);
          this.activitiesState.set('ready');
        },
        error: () => this.activitiesState.set('error'),
      });
  }

  private loadSubmissions(work: WorkDetail, isAssignee: boolean): void {
    this.submissionApi
      .getSubmissionsByWork(work.id)
      .pipe(
        switchMap((list) => {
          const latest = [...list].sort((a, b) => b.submissionNo - a.submissionNo)[0];
          // Only the person who submitted can open the reconciliation (it is a 403 for anyone else).
          if (!isAssignee || !latest)
            return of({ list, detail: null as MySubmissionDetail | null });
          return this.mySubmissions.detail(latest.id).pipe(
            map((detail) => ({ list, detail: detail as MySubmissionDetail | null })),
            catchError(() => of({ list, detail: null as MySubmissionDetail | null })),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ list, detail }) => {
          this.submissions.set(this.toRows(work, list, detail));
          this.submissionsState.set('ready');
        },
        error: () => this.submissionsState.set('error'),
      });
  }

  private toRows(
    work: WorkDetail,
    list: SubmissionDetail[],
    detail: MySubmissionDetail | null,
  ): SubmissionRow[] {
    const text = this.localization.work.detail.submissions;
    const latestNo = Math.max(0, ...list.map((s) => s.submissionNo));
    return [...list]
      .sort((a, b) => b.submissionNo - a.submissionNo)
      .map((submission) => {
        const version = detail?.versions.find((v) => v.submissionId === submission.id);
        const codes =
          detail && submission.submissionNo === latestNo
            ? detail.transactions.map((t) => t.transactionCode)
            : [];
        if (work.customerCif) codes.push(`CIF ${work.customerCif}`);
        const known = (text.status as Record<string, string>)[submission.status];
        return {
          id: submission.id,
          versionNo: submission.submissionNo,
          submittedAt: submission.submittedAt,
          codes: codes.join(', '),
          label: (version ? text.match[version.matchStatus] : null) ?? known ?? submission.status,
          tone: version ? this.toneOf(version.matchStatus) : 'gray',
        };
      });
  }

  private toneOf(match: string): Tone {
    if (match === 'MATCHED') return 'green';
    if (match === 'NOT_MATCHED') return 'red';
    return 'gray';
  }

  private toListItem(work: WorkDetail): WorkListItem {
    return {
      id: work.id,
      code: work.code,
      title: work.title,
      workTypeId: work.workTypeId,
      workTypeName: work.workTypeName,
      businessCampaignId: work.businessCampaignId,
      assigneeEmployeeId: work.assigneeEmployeeId,
      assigneeEmployeeCode: work.assigneeEmployeeCode,
      assigneeEmployeeName: work.assigneeEmployeeName,
      departmentId: null,
      departmentCode: null,
      departmentName: null,
      priority: work.priority,
      status: work.status,
      plannedStartDate: work.plannedStartDate,
      plannedEndDate: work.plannedEndDate,
      updatedAt: work.updatedAt,
    };
  }

  private showNotice(kind: Notice['kind'], text: string): void {
    clearTimeout(this.noticeTimer);
    this.notice.set({ kind, text });
    this.noticeTimer = this.zone.runOutsideAngular(() =>
      setTimeout(() => this.zone.run(() => this.notice.set(null)), NOTICE_MS),
    );
  }
}
