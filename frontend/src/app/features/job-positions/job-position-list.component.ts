import { Component, DestroyRef, HostListener, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, EMPTY, finalize, Subject, switchMap } from 'rxjs';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { JobPositionService } from '../../core/job-position.service';
import { JobPositionFilter, JobPositionSummary } from '../../core/job-position.models';
import { displayNumber } from '../../shared/ui/display';
import { JobPositionDetailDrawerComponent } from './job-position-detail-drawer.component';

/** SCR-ORG-02: read-only job position list with the SCR-ORG-02-D1 detail drawer. */
@Component({
  selector: 'app-job-position-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    HeaderComponent,
    SidebarComponent,
    JobPositionDetailDrawerComponent,
  ],
  templateUrl: './job-position-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './job-position-list.component.scss',
  ],
})
export class JobPositionListComponent extends LocalizedComponent {
  private readonly api = inject(JobPositionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  protected readonly positions = signal<JobPositionSummary[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly selectedPositionId = signal<string | null>(null);

  protected keyword = '';
  protected status = '';
  protected page = 1;
  protected readonly pageSize = 50;

  protected readonly selectedPosition = computed(
    () => this.positions().find((p) => p.id === this.selectedPositionId()) ?? null,
  );

  /** Latest sync time across the loaded rows; null when none carries a valid timestamp. */
  protected readonly lastSyncAt = computed(() => {
    const times = this.positions()
      .map((p) => Date.parse(p.lastSyncAt))
      .filter((time) => !Number.isNaN(time));
    return times.length ? new Date(Math.max(...times)).toISOString() : null;
  });

  /** Banner text built from the response only; N/A while there is no sync time, never a made-up date. */
  protected readonly syncText = computed(() => {
    const last = this.lastSyncAt();
    return this.i18n.translate('jobPositions.subtitleBanner', {
      total: this.totalCount(),
      lastSyncAt: last
        ? this.i18n.formatDate(last, 'DD/MM/YYYY HH:mm')
        : this.localization.department.notAvailable,
    });
  });

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          const filter: JobPositionFilter = {
            keyword: this.keyword.trim() || undefined,
            status: this.status || undefined,
            page: this.page,
            pageSize: this.pageSize,
          };
          return this.api.list(filter).pipe(
            catchError(() => {
              this.failed.set(true);
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((res) => {
        this.positions.set(res.items);
        this.totalCount.set(res.totalCount);
        const selected = this.selectedPositionId();
        if (selected && !res.items.some((p) => p.id === selected))
          this.selectedPositionId.set(null);
      });

    this.reload.next();
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (this.selectedPositionId() !== null) {
      this.closeDrawer();
    }
  }

  protected refresh(): void {
    this.page = 1;
    this.reload.next();
  }

  protected selectPosition(position: JobPositionSummary): void {
    this.selectedPositionId.set(this.selectedPositionId() === position.id ? null : position.id);
  }

  protected closeDrawer(): void {
    this.selectedPositionId.set(null);
  }

  protected number(val: number | null | undefined): string {
    return displayNumber(
      val,
      this.i18n.currentLanguage(),
      this.localization.department.notAvailable,
    );
  }
}
