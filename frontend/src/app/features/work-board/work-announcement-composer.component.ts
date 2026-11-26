import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { CampaignService } from '../../core/campaign.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  AnnouncementRelatedType,
  NotificationRecipientOption,
  NotificationSeverity,
  SendNotificationRequest,
} from '../../core/notification.models';
import { NotificationService } from '../../core/notification.service';
import { WorkService } from '../../core/work.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';

export type AnnouncementTargetMode = 'all' | 'position' | 'employees';
export type RelatedLinkChoice = 'NONE' | AnnouncementRelatedType;

export interface PositionGroupOption {
  title: string;
  employeeIds: string[];
  count: number;
}

export interface RelatedRecordOption {
  id: string;
  code: string;
  label: string;
}

export interface SessionSentAnnouncement {
  title: string;
  severity: NotificationSeverity;
  recipientCount: number;
  targetSummary: string;
  relatedLabel: string | null;
  sentAt: string;
}

const TITLE_MAX = 200;
const BODY_MAX = 2000;
const RELATED_PAGE_SIZE = 50;
const TIME_FORMAT = 'HH:mm DD/MM';

/**
 * SCR-WRK-07: Team Announcement Composer.
 * Lets managers compose an in-app announcement to everyone in scope, by job position, or to picked
 * employees, optionally linked to a work item or campaign. Contract: docs/api/NOTIFICATION_CONTRACT.md.
 */
@Component({
  selector: 'app-work-announcement-composer',
  standalone: true,
  imports: [RouterLink, HeaderComponent, SidebarComponent],
  templateUrl: './work-announcement-composer.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './work-announcement-composer.component.scss',
    './work-announcement-composer.component.part2.scss',
  ],
})
export class WorkAnnouncementComposerComponent extends LocalizedComponent {
  private readonly notifications = inject(NotificationService);
  private readonly works = inject(WorkService);
  private readonly campaigns = inject(CampaignService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  private recipientsSub?: Subscription;
  private relatedSub?: Subscription;

  protected readonly titleMax = TITLE_MAX;
  protected readonly bodyMax = BODY_MAX;

  protected readonly targetMode = signal<AnnouncementTargetMode>('all');
  protected readonly recipients = signal<NotificationRecipientOption[]>([]);
  protected readonly recipientsLoading = signal(true);
  protected readonly recipientsFailed = signal(false);

  protected readonly positionSearch = signal('');
  protected readonly selectedPositions = signal<ReadonlySet<string>>(new Set());
  protected readonly employeeSearch = signal('');
  protected readonly selectedEmployeeIds = signal<ReadonlySet<string>>(new Set());

  protected readonly title = signal('');
  protected readonly titleTouched = signal(false);
  protected readonly body = signal('');
  protected readonly severity = signal<NotificationSeverity>('NORMAL');

  protected readonly relatedType = signal<RelatedLinkChoice>('NONE');
  protected readonly relatedId = signal('');
  protected readonly relatedOptions = signal<RelatedRecordOption[]>([]);
  protected readonly relatedLoading = signal(false);
  protected readonly relatedFailed = signal(false);

  protected readonly sending = signal(false);
  protected readonly sendError = signal<string | null>(null);
  protected readonly sentCount = signal<number | null>(null);
  protected readonly sentHistory = signal<SessionSentAnnouncement[]>([]);

  protected readonly senderDisplayName = computed(
    () =>
      this.auth.currentUser()?.fullName?.trim() ||
      this.localization.work.announcement.previewDefaultSender,
  );

  protected readonly positionGroups = computed<PositionGroupOption[]>(() => {
    const fallback = this.localization.work.announcement.unassignedPosition;
    const map = new Map<string, string[]>();
    for (const person of this.recipients()) {
      const label = person.jobPositionTitle?.trim() || fallback;
      const list = map.get(label) ?? [];
      list.push(person.id);
      map.set(label, list);
    }
    return Array.from(map.entries())
      .map(([title, employeeIds]) => ({ title, employeeIds, count: employeeIds.length }))
      .sort((a, b) => a.title.localeCompare(b.title, 'vi'));
  });

  protected readonly visiblePositions = computed(() => {
    const q = this.positionSearch().trim().toLowerCase();
    if (!q) return this.positionGroups();
    return this.positionGroups().filter((g) => g.title.toLowerCase().includes(q));
  });

  protected readonly visibleEmployees = computed(() => {
    const q = this.employeeSearch().trim().toLowerCase();
    if (!q) return this.recipients();
    return this.recipients().filter((r) =>
      `${r.fullName} ${r.employeeCode} ${r.departmentName} ${r.jobPositionTitle}`
        .toLowerCase()
        .includes(q),
    );
  });

  protected readonly selectedEmployeesList = computed(() => {
    const picked = this.selectedEmployeeIds();
    return this.recipients().filter((r) => picked.has(r.id));
  });

  protected readonly selectedPositionsList = computed(() => Array.from(this.selectedPositions()));

  protected readonly resolvedRecipientIds = computed<string[]>(() => {
    const mode = this.targetMode();
    if (mode === 'all') return this.recipients().map((r) => r.id);
    if (mode === 'employees') return Array.from(this.selectedEmployeeIds());
    const chosen = this.selectedPositions();
    const ids = new Set<string>();
    for (const group of this.positionGroups()) {
      if (chosen.has(group.title)) {
        for (const id of group.employeeIds) ids.add(id);
      }
    }
    return Array.from(ids);
  });

  protected readonly effectiveRecipientCount = computed(() => this.resolvedRecipientIds().length);

  protected readonly trimmedTitle = computed(() => this.title().trim());
  protected readonly titleMissing = computed(() => this.trimmedTitle().length === 0);
  protected readonly titleTooLong = computed(() => this.trimmedTitle().length > TITLE_MAX);
  protected readonly bodyTooLong = computed(() => this.body().length > BODY_MAX);
  protected readonly relatedIncomplete = computed(
    () => this.relatedType() !== 'NONE' && !this.relatedId().trim(),
  );

  protected readonly selectedRelatedLabel = computed(() => {
    if (this.relatedType() === 'NONE' || !this.relatedId()) return null;
    const match = this.relatedOptions().find((opt) => opt.id === this.relatedId());
    return match ? `${match.code} · ${match.label}` : null;
  });

  protected readonly sendBlocked = computed(
    () =>
      this.sending() ||
      this.recipientsLoading() ||
      this.effectiveRecipientCount() === 0 ||
      this.titleMissing() ||
      this.titleTooLong() ||
      this.bodyTooLong() ||
      this.relatedIncomplete(),
  );

  constructor() {
    super();
    this.destroyRef.onDestroy(() => {
      this.recipientsSub?.unsubscribe();
      this.relatedSub?.unsubscribe();
    });
    this.loadRecipients();
    this.applyInitialQueryParams();
  }

  protected loadRecipients(): void {
    this.recipientsSub?.unsubscribe();
    this.recipientsLoading.set(true);
    this.recipientsFailed.set(false);
    this.recipientsSub = this.notifications
      .recipients()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.recipients.set(list);
          this.recipientsLoading.set(false);
        },
        error: () => {
          this.recipients.set([]);
          this.recipientsFailed.set(true);
          this.recipientsLoading.set(false);
        },
      });
  }

  protected chooseTargetMode(mode: AnnouncementTargetMode): void {
    this.targetMode.set(mode);
    this.sendError.set(null);
  }

  protected togglePosition(positionTitle: string): void {
    this.selectedPositions.update((current) => {
      const next = new Set(current);
      if (!next.delete(positionTitle)) next.add(positionTitle);
      return next;
    });
  }

  protected clearPositions(): void {
    this.selectedPositions.set(new Set());
  }

  protected toggleEmployee(employeeId: string): void {
    this.selectedEmployeeIds.update((current) => {
      const next = new Set(current);
      if (!next.delete(employeeId)) next.add(employeeId);
      return next;
    });
  }

  protected pickAllVisibleEmployees(): void {
    this.selectedEmployeeIds.update((current) => {
      const next = new Set(current);
      for (const emp of this.visibleEmployees()) next.add(emp.id);
      return next;
    });
  }

  protected clearEmployees(): void {
    this.selectedEmployeeIds.set(new Set());
  }

  protected onRelatedTypeChange(nextType: RelatedLinkChoice): void {
    this.relatedType.set(nextType);
    this.relatedId.set('');
    this.relatedOptions.set([]);
    this.relatedFailed.set(false);
    if (nextType === 'NONE') {
      this.relatedSub?.unsubscribe();
      this.relatedLoading.set(false);
      return;
    }
    this.loadRelatedOptions(nextType);
  }

  protected loadRelatedOptions(type: AnnouncementRelatedType): void {
    this.relatedSub?.unsubscribe();
    this.relatedLoading.set(true);
    this.relatedFailed.set(false);

    if (type === 'WORK') {
      this.relatedSub = this.works
        .list({ page: 1, pageSize: RELATED_PAGE_SIZE })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (page) => {
            this.relatedOptions.set(
              page.items.map((w) => ({ id: w.id, code: w.code, label: w.title })),
            );
            this.relatedLoading.set(false);
          },
          error: () => {
            this.relatedOptions.set([]);
            this.relatedFailed.set(true);
            this.relatedLoading.set(false);
          },
        });
      return;
    }

    this.relatedSub = this.campaigns
      .list({ page: 1, pageSize: RELATED_PAGE_SIZE })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (overview) => {
          this.relatedOptions.set(
            overview.campaigns.items.map((c) => ({ id: c.id, code: c.code, label: c.name })),
          );
          this.relatedLoading.set(false);
        },
        error: () => {
          this.relatedOptions.set([]);
          this.relatedFailed.set(true);
          this.relatedLoading.set(false);
        },
      });
  }

  protected onPositionSearchInput(event: Event): void {
    this.positionSearch.set((event.target as HTMLInputElement).value);
  }

  protected onEmployeeSearchInput(event: Event): void {
    this.employeeSearch.set((event.target as HTMLInputElement).value);
  }

  protected onTitleInput(event: Event): void {
    this.title.set((event.target as HTMLInputElement).value);
    this.sendError.set(null);
  }

  protected onBodyInput(event: Event): void {
    this.body.set((event.target as HTMLTextAreaElement).value);
  }

  protected onSeverityChange(event: Event): void {
    this.severity.set((event.target as HTMLSelectElement).value as NotificationSeverity);
  }

  protected onRelatedTypeSelect(event: Event): void {
    this.onRelatedTypeChange((event.target as HTMLSelectElement).value as RelatedLinkChoice);
  }

  protected onRelatedIdChange(event: Event): void {
    this.relatedId.set((event.target as HTMLSelectElement).value);
  }

  protected onFormSubmit(event: Event): void {
    event.preventDefault();
    this.submit();
  }

  protected onFormKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
      event.preventDefault();
      this.submit();
    }
  }

  protected cancel(): void {
    void this.router.navigate(['/my-work'], { queryParams: { view: 'monitor' } });
  }

  protected submit(): void {
    this.titleTouched.set(true);
    if (this.sendBlocked()) return;

    const request = this.buildRequest();
    const targetSummary = this.buildTargetSummary();
    const relatedLabel = this.selectedRelatedLabel();

    this.sending.set(true);
    this.sendError.set(null);
    this.sentCount.set(null);

    this.notifications
      .send(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (count) => {
          this.sending.set(false);
          this.sentCount.set(count);
          this.sentHistory.update((history) => [
            {
              title: request.title,
              severity: request.severity ?? 'NORMAL',
              recipientCount: count,
              targetSummary,
              relatedLabel,
              sentAt: this.i18n.formatDate(new Date(), TIME_FORMAT),
            },
            ...history,
          ]);
          this.resetFormAfterSend();
        },
        error: (error: HttpErrorResponse) => {
          this.sending.set(false);
          const payload =
            (error?.error as { errors?: string[]; message?: string } | undefined) ?? {};
          this.sendError.set(
            error?.status === 403
              ? this.localization.work.announcement.sendForbidden
              : (payload.errors?.[0] ??
                  payload.message ??
                  this.localization.work.announcement.sendFailed),
          );
        },
      });
  }

  private buildRequest(): SendNotificationRequest {
    const mode = this.targetMode();
    const relType = this.relatedType();
    const request: SendNotificationRequest = {
      title: this.trimmedTitle(),
      body: this.body().trim() || undefined,
      severity: this.severity(),
    };
    if (mode !== 'all') {
      request.recipientEmployeeIds = this.resolvedRecipientIds();
    }
    if (relType !== 'NONE' && this.relatedId().trim()) {
      request.relatedType = relType;
      request.relatedId = this.relatedId().trim();
    }
    return request;
  }

  private buildTargetSummary(): string {
    const mode = this.targetMode();
    if (mode === 'all') {
      return this.i18n.translate('work.announcement.targetAll', {
        count: this.recipients().length,
      });
    }
    if (mode === 'position') {
      return this.selectedPositionsList().join(', ');
    }
    return this.i18n.translate('work.announcement.employeesSelectedSummary', {
      count: this.selectedEmployeeIds().size,
    });
  }

  private resetFormAfterSend(): void {
    this.title.set('');
    this.titleTouched.set(false);
    this.body.set('');
    this.severity.set('NORMAL');
    this.relatedType.set('NONE');
    this.relatedId.set('');
    this.relatedOptions.set([]);
  }

  private applyInitialQueryParams(): void {
    const params = this.route.snapshot.queryParamMap;
    const relType = params.get('relatedType')?.toUpperCase();
    const relId = params.get('relatedId')?.trim();
    if (relType === 'WORK' || relType === 'CAMPAIGN') {
      this.relatedType.set(relType);
      if (relId) this.relatedId.set(relId);
      this.loadRelatedOptions(relType);
    }
  }
}
