import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  TemplateRef,
  computed,
  contentChildren,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { EmptyStateComponent } from './empty-state.component';
import { SkeletonComponent } from './skeleton.component';

export interface TableColumn {
  readonly key: string;
  readonly header: string;
  readonly sortable?: boolean;
  readonly align?: 'start' | 'end';
  readonly mono?: boolean;
  readonly width?: string;
}

export interface SortState {
  readonly key: string;
  readonly direction: 'asc' | 'desc';
}

export interface TableState {
  readonly sort: SortState | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface CellContext {
  $implicit: object;
  row: object;
  value: unknown;
}

/** Custom cell content: `<ng-template appCell="status" let-row>…</ng-template>`. */
@Directive({ selector: 'ng-template[appCell]' })
export class DataTableCellDirective {
  readonly key = input.required<string>({ alias: 'appCell' });
  readonly template = inject<TemplateRef<CellContext>>(TemplateRef);
}

const SKELETON_ROWS = 5;

/**
 * Paginated, sortable data table. In `client` mode (default) it sorts and slices `rows` itself;
 * in `server` mode `rows` is the current page and the parent reacts to `stateChange`.
 */
@Component({
  selector: 'app-data-table',
  imports: [NgTemplateOutlet, EmptyStateComponent, SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './data-table.component.html',
  styleUrl: './data-table.component.scss',
})
export class DataTableComponent<T extends object> {
  readonly columns = input.required<readonly TableColumn[]>();
  readonly rows = input<readonly T[]>([]);
  readonly caption = input.required<string>();
  readonly trackBy = input<(row: T) => unknown>((row) => row);
  readonly mode = input<'client' | 'server'>('client');
  readonly totalItems = input<number | null>(null);
  readonly pageSize = input(10);
  readonly loading = input(false);
  readonly errorMessage = input('');
  readonly rowClickable = input(false);
  readonly rowLabel = input<(row: T) => string>(() => '');
  readonly emptyTitle = input('Không có dữ liệu');
  readonly emptyDescription = input('');

  readonly stateChange = output<TableState>();
  readonly rowClick = output<T>();
  readonly retry = output<void>();

  protected readonly cellTemplates = contentChildren(DataTableCellDirective);
  protected readonly skeletonRows = Array.from({ length: SKELETON_ROWS }, (_, index) => index);

  protected readonly sort = signal<SortState | null>(null);
  private readonly requestedPage = signal(1);

  protected readonly total = computed(() =>
    this.mode() === 'server' ? (this.totalItems() ?? this.rows().length) : this.rows().length,
  );
  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.total() / this.pageSize())),
  );
  protected readonly page = computed(() => Math.min(this.requestedPage(), this.pageCount()));

  protected readonly visibleRows = computed<readonly T[]>(() => {
    if (this.mode() === 'server') {
      return this.rows();
    }
    const sort = this.sort();
    const ordered = sort
      ? [...this.rows()].sort((a, b) => compareValues(a, b, sort.key) * dir(sort))
      : this.rows();
    const start = (this.page() - 1) * this.pageSize();
    return ordered.slice(start, start + this.pageSize());
  });

  protected readonly rangeLabel = computed(() => {
    const total = this.total();
    if (total === 0) {
      return '0 kết quả';
    }
    const start = (this.page() - 1) * this.pageSize() + 1;
    const end = Math.min(total, start + this.visibleRows().length - 1);
    return `Hiển thị ${start}–${end} / ${total}`;
  });

  protected template(key: string): TemplateRef<CellContext> | null {
    return this.cellTemplates().find((cell) => cell.key() === key)?.template ?? null;
  }

  protected cellValue(row: T, key: string): unknown {
    return (row as Record<string, unknown>)[key];
  }

  protected context(row: T, key: string): CellContext {
    return { $implicit: row, row, value: this.cellValue(row, key) };
  }

  protected ariaSort(column: TableColumn): 'ascending' | 'descending' | 'none' | null {
    if (!column.sortable) {
      return null;
    }
    const sort = this.sort();
    if (sort?.key !== column.key) {
      return 'none';
    }
    return sort.direction === 'asc' ? 'ascending' : 'descending';
  }

  protected toggleSort(column: TableColumn): void {
    const current = this.sort();
    let next: SortState | null;
    if (current?.key !== column.key) {
      next = { key: column.key, direction: 'asc' };
    } else if (current.direction === 'asc') {
      next = { key: column.key, direction: 'desc' };
    } else {
      next = null;
    }
    this.sort.set(next);
    this.requestedPage.set(1);
    this.emitState();
  }

  protected goTo(page: number): void {
    const target = Math.min(Math.max(1, page), this.pageCount());
    if (target === this.page()) {
      return;
    }
    this.requestedPage.set(target);
    this.emitState();
  }

  protected onRowKeydown(event: KeyboardEvent, row: T): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.rowClick.emit(row);
    }
  }

  private emitState(): void {
    this.stateChange.emit({ sort: this.sort(), page: this.page(), pageSize: this.pageSize() });
  }
}

function dir(sort: SortState): 1 | -1 {
  return sort.direction === 'asc' ? 1 : -1;
}

function compareValues(a: object, b: object, key: string): number {
  const left = (a as Record<string, unknown>)[key];
  const right = (b as Record<string, unknown>)[key];
  if (typeof left === 'number' && typeof right === 'number') {
    return left - right;
  }
  return String(left ?? '').localeCompare(String(right ?? ''), 'vi', { numeric: true });
}
