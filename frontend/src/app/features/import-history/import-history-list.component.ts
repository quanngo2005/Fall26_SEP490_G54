import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ImportBatchSummary } from '../../core/import-history.models';
import { ImportHistoryService } from '../../core/import-history.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { AppDatePipe } from '../../shared/pipes';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';

/** SCR-ADM-07: import history list. Filters are rendered but disabled pending API filter support. */
@Component({
  selector: 'app-import-history-list',
  imports: [AppDatePipe, HeaderComponent, SidebarComponent],
  templateUrl: './import-history-list.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    './import-history-list.component.scss',
  ],
})
export class ImportHistoryListComponent extends LocalizedComponent {
  private readonly api = inject(ImportHistoryService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly dateTime = 'DD/MM/YYYY HH:mm';

  protected readonly items = signal<ImportBatchSummary[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly selectedId = signal<string | null>(null);

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())),
  );
  protected readonly pages = computed(() => {
    const count = this.pageCount();
    const start = Math.max(1, Math.min(this.page() - 1, count - 2));
    return Array.from({ length: Math.min(3, count) }, (_, index) => start + index);
  });
  protected readonly range = computed(() => {
    const total = this.totalCount();
    const from = total === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1;
    const to = Math.min(total, this.page() * this.pageSize());
    return this.localization.importHistory.range
      .replace('{from}', String(from))
      .replace('{to}', String(to))
      .replace('{total}', String(total));
  });

  constructor() {
    super();
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api
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

  protected selectRow(batch: ImportBatchSummary): void {
    this.selectedId.set(batch.id === this.selectedId() ? null : batch.id);
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

  protected statusClass(status: string): string {
    const classes: Record<string, string> = {
      COMPLETED: 'is-success',
      COMPLETED_WITH_ERRORS: 'is-warning',
      REJECTED: 'is-danger',
    };
    return classes[status] ?? 'is-neutral';
  }
}
