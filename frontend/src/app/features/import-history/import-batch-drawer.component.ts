import { Component, DestroyRef, HostListener, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, expand, Observable, reduce, Subject, switchMap, tap } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  ImportBatchSummary,
  ImportedRating,
  ImportedTransaction,
  ImportFileType,
  ImportRowError,
} from '../../core/import-history.models';
import { ImportHistoryService } from '../../core/import-history.service';
import { ServiceRatingImportService } from '../../core/service-rating-import.service';
import { PagedResult } from '../../core/paged-result.model';
import { AppDatePipe, AppNumberPipe } from '../../shared/pipes';
import { TablePaginationComponent } from '../../shared/ui';
import { saveFile } from '../performance-dashboard/performance-page';
import { importErrorsCsv } from './import-errors-csv';

/** `rows`: the saved rows of the batch (transactions or ratings, by file type). */
type ImportBatchTab = 'errors' | 'rows';

/** Rows per page of the two drawer tables. */
const DRAWER_PAGE_SIZE = 20;

/** The largest page the API serves, used to fetch every rejected row for the CSV in few requests. */
const EXPORT_PAGE_SIZE = 200;

/** One server-paged table of the drawer: the shown page, its rows and its load state. */
class PagedTable<T> {
  readonly page = signal(1);
  readonly result = signal<PagedResult<T> | null>(null);
  readonly loading = signal(false);
  readonly failed = signal(false);
  private readonly requests = new Subject<number>();

  constructor(fetch: (page: number) => Observable<PagedResult<T>>, destroyRef: DestroyRef) {
    this.requests
      .pipe(
        switchMap((page) => {
          this.page.set(page);
          this.loading.set(true);
          this.failed.set(false);
          return fetch(page).pipe(
            tap((result) => {
              this.result.set(result);
              this.loading.set(false);
            }),
            catchError(() => {
              this.loading.set(false);
              this.failed.set(true);
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe();
  }

  load(page: number): void {
    this.requests.next(page);
  }

  reset(): void {
    this.result.set(null);
    this.page.set(1);
    this.failed.set(false);
  }
}

/**
 * SCR-ADM-07-D1: the right-hand drawer of an import batch with its rejected rows and its saved rows (UC33): transactions
 * for a transaction batch, ratings for a service rating batch (UC32).
 */
@Component({
  selector: 'app-import-batch-drawer',
  imports: [AppDatePipe, AppNumberPipe, TablePaginationComponent],
  templateUrl: './import-batch-drawer.component.html',
  styleUrls: ['../../shared/ui/ui-controls.scss', './import-batch-drawer.component.scss'],
})
export class ImportBatchDrawerComponent extends LocalizedComponent {
  private readonly transactionApi = inject(ImportHistoryService);
  private readonly ratingApi = inject(ServiceRatingImportService);
  private readonly destroyRef = inject(DestroyRef);

  readonly batchId = input.required<string>();
  readonly fileType = input<ImportFileType>('TRANSACTION');
  readonly closed = output<void>();

  protected readonly dateTime = 'DD/MM/YYYY HH:mm';
  protected readonly pageSize = DRAWER_PAGE_SIZE;

  protected readonly batch = signal<ImportBatchSummary | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly tab = signal<ImportBatchTab>('errors');
  protected readonly exporting = signal(false);
  protected readonly exportFailed = signal(false);

  protected readonly errors = new PagedTable<ImportRowError>(
    (page) => this.api().errors(this.batchId(), page, DRAWER_PAGE_SIZE),
    this.destroyRef,
  );
  protected readonly transactions = new PagedTable<ImportedTransaction>(
    (page) => this.transactionApi.transactions(this.batchId(), page, DRAWER_PAGE_SIZE),
    this.destroyRef,
  );
  protected readonly ratings = new PagedTable<ImportedRating>(
    (page) => this.ratingApi.ratings(this.batchId(), page, DRAWER_PAGE_SIZE),
    this.destroyRef,
  );

  constructor() {
    super();
    toObservable(this.batchId)
      .pipe(
        switchMap((id) => {
          this.batch.set(null);
          this.failed.set(false);
          this.exportFailed.set(false);
          this.tab.set('errors');
          this.errors.reset();
          this.transactions.reset();
          this.ratings.reset();
          this.loading.set(true);
          this.errors.load(1);
          return this.api()
            .detail(id)
            .pipe(
              tap((batch) => {
                this.batch.set(batch);
                this.loading.set(false);
              }),
              catchError(() => {
                this.loading.set(false);
                this.failed.set(true);
                return EMPTY;
              }),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  protected close(): void {
    this.closed.emit();
  }

  protected isRating(): boolean {
    return this.fileType() === 'SERVICE_RATING';
  }

  /** The saved rows are read the first time their tab is shown. */
  protected showTab(tab: ImportBatchTab): void {
    this.tab.set(tab);
    const rows = this.isRating() ? this.ratings : this.transactions;
    if (tab === 'rows' && !rows.result()) rows.load(1);
  }

  /** The batch endpoints of the drawer's file type: both serve the same batch and error shapes. */
  private api(): ImportHistoryService | ServiceRatingImportService {
    return this.isRating() ? this.ratingApi : this.transactionApi;
  }

  protected errorCodeLabel(code: string): string {
    return (
      (this.localization.importHistory.drawer.errorCodes as Record<string, string>)[code] ?? code
    );
  }

  /** Reads every page of rejected rows one after the other and saves them as a CSV file. */
  protected downloadErrors(): void {
    const batch = this.batch();
    if (!batch || this.exporting()) return;
    this.exporting.set(true);
    this.exportFailed.set(false);
    const fetch = (page: number) => this.api().errors(batch.id, page, EXPORT_PAGE_SIZE);
    fetch(1)
      .pipe(
        expand((result) =>
          result.items.length > 0 && result.page * result.pageSize < result.totalCount
            ? fetch(result.page + 1)
            : EMPTY,
        ),
        reduce((rows, result) => rows.concat(result.items), [] as ImportRowError[]),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (rows) => {
          const t = this.localization.importHistory.drawer;
          const headers = [t.colRow, t.colField, t.colCode, t.colMessage];
          saveFile(
            importErrorsCsv(headers, rows),
            `${batch.fileName.replace(/\.xlsx$/i, '')}-errors.csv`,
          );
          this.exporting.set(false);
        },
        error: () => {
          this.exporting.set(false);
          this.exportFailed.set(true);
        },
      });
  }

  @HostListener('document:keydown.escape', ['$event'])
  protected onEscape(event: Event): void {
    event.preventDefault();
    this.close();
  }
}
