import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ImportBatchSummary, ImportFileType } from '../../core/import-history.models';
import { ImportHistoryService } from '../../core/import-history.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { RouteStateService } from '../../core/route-state.service';
import { ServiceRatingImportService } from '../../core/service-rating-import.service';
import { canManageServiceRatingImport } from '../../core/transaction-import.guard';
import { AppDatePipe } from '../../shared/pipes';
import { HeaderComponent, LayoutIconComponent, SidebarComponent } from '../../shared/layout';
import {
  SelectOption,
  StatusBadgeComponent,
  StatusBadgeTone,
  TablePaginationComponent,
  UiSelectComponent,
} from '../../shared/ui';
import { ImportBatchDrawerComponent } from './import-batch-drawer.component';

/**
 * SCR-ADM-07: import history list. The file type picks the history (transactions, or service ratings with
 * `service_rating_import:manage`); the other filters are rendered but disabled pending API filter support. Selecting a
 * row opens its details drawer (ADM-07-D1). Navigation state `importFileType` / `importBatchId` opens a given batch
 * (a 1-star rating notification, the import result).
 */
@Component({
  selector: 'app-import-history-list',
  imports: [
    RouterLink,
    AppDatePipe,
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    StatusBadgeComponent,
    TablePaginationComponent,
    UiSelectComponent,
    ImportBatchDrawerComponent,
  ],
  templateUrl: './import-history-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './import-history-list.component.scss',
  ],
})
export class ImportHistoryListComponent extends LocalizedComponent {
  private readonly api = inject(ImportHistoryService);
  private readonly ratingApi = inject(ServiceRatingImportService);
  private readonly routeState = inject(RouteStateService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly canViewRatings = canManageServiceRatingImport(
    inject(AuthService).currentUser()?.permissions,
  );

  protected readonly dateTime = 'DD/MM/YYYY HH:mm';

  /** Filters other than the file type are not wired yet, so those dropdowns only show their placeholder. */
  protected readonly noOptions: readonly SelectOption[] = [];

  protected readonly fileType = signal<ImportFileType>(
    this.canViewRatings && this.routeState.value('importFileType') === 'SERVICE_RATING'
      ? 'SERVICE_RATING'
      : 'TRANSACTION',
  );
  protected readonly fileTypeOptions = computed<SelectOption<ImportFileType>[]>(() => {
    const t = this.localization.importHistory;
    return [
      { value: 'TRANSACTION', label: t.fileTypeTransaction },
      ...(this.canViewRatings
        ? [{ value: 'SERVICE_RATING' as const, label: t.fileTypeServiceRating }]
        : []),
    ];
  });
  protected readonly fileTypeLabel = computed(() =>
    this.fileType() === 'SERVICE_RATING'
      ? this.localization.importHistory.fileTypeServiceRating
      : this.localization.importHistory.fileTypeTransaction,
  );

  protected readonly items = signal<ImportBatchSummary[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly selectedId = signal<string | null>(this.routeState.value('importBatchId'));

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())),
  );

  constructor() {
    super();
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    const source = this.fileType() === 'SERVICE_RATING' ? this.ratingApi : this.api;
    source
      .list(this.page(), this.pageSize())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.failed.set(true);
        },
      });
  }

  protected goTo(page: number): void {
    if (page < 1 || page > this.pageCount() || page === this.page()) return;
    this.page.set(page);
    this.load();
  }

  protected selectFileType(type: ImportFileType): void {
    if (type === this.fileType()) return;
    this.fileType.set(type);
    this.selectedId.set(null);
    this.page.set(1);
    this.load();
  }

  protected selectRow(batch: ImportBatchSummary): void {
    this.selectedId.set(batch.id);
  }

  protected batchCode(batch: ImportBatchSummary): string {
    return batch.id.slice(0, 8).toUpperCase();
  }

  protected statusLabel(status: string): string {
    const t = this.localization.importHistory;
    const labels: Record<string, string> = {
      COMPLETED: t.statusCompleted,
      COMPLETED_WITH_ERRORS: t.statusCompletedWithErrors,
      REJECTED: t.statusRejected,
    };
    return labels[status] ?? status;
  }

  protected statusTone(status: string): StatusBadgeTone {
    const tones: Record<string, StatusBadgeTone> = {
      COMPLETED: 'success',
      COMPLETED_WITH_ERRORS: 'warning',
      REJECTED: 'danger',
    };
    return tones[status] ?? 'neutral';
  }
}
