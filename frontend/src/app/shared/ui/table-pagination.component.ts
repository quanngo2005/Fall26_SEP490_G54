import { Component, computed, input, output } from '@angular/core';
import { SelectOption, UiSelectComponent } from './ui-select.component';

/**
 * Canonical Enterprise Table Footer & Pagination bar.
 * Centralizes page window calculation, range formatting, prev/next buttons, and optional page-size selector.
 */
@Component({
  selector: 'app-table-pagination',
  imports: [UiSelectComponent],
  template: `
    <footer class="ui-pagination-footer">
  <span class="ui-pagination-range">{{summary()}}</span>

  <div class="ui-pagination-controls">
    @if (pageCount() > 1 || alwaysShowPages()) {
      <nav class="ui-pagination-pages" [attr.aria-label]="ariaLabel()">
        <button
          type="button"
          class="ui-page-btn"
          [disabled]="disabled() || page() <= 1"
          (click)="selectPage(page() - 1)"
          aria-label="Previous page"
        >
          ‹
        </button>

        @for (item of pageItems(); track $index) {
          @if (item === 'ellipsis') {
            <span class="ui-page-ellipsis" aria-hidden="true">…</span>
          } @else {
            <button
              type="button"
              class="ui-page-btn"
              [class.is-active]="item === page()"
              [disabled]="disabled()"
              [attr.aria-current]="item === page() ? 'page' : null"
              (click)="selectPage(item)"
            >
              {{item}}
            </button>
          }
        }

        <button
          type="button"
          class="ui-page-btn"
          [disabled]="disabled() || page() >= pageCount()"
          (click)="selectPage(page() + 1)"
          aria-label="Next page"
        >
          ›
        </button>
      </nav>
    }

    @if (sizeSelectOptions().length > 0) {
      <app-ui-select
        class="ui-size-dropdown"
        size="sm"
        [options]="sizeSelectOptions()"
        [value]="pageSize()"
        [disabled]="disabled()"
        [ariaLabel]="pageSizeAriaLabel()"
        (valueChange)="onSizeChange($event)"
      />
    }
  </div>
</footer>
  `,
  styles: [
    `
      :host {
        display: block;
      }

      .ui-pagination-footer {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: 10px 16px;
        border-top: 1px solid #f1f5f9;
        border-radius: 0 0 8px 8px;
        background: #fff;
        color: #64748b;
        font:
          400 12px/18px 'Nunito Sans',
          sans-serif;
        padding: 10px 14px;
      }

      .ui-pagination-range {
        font-variant-numeric: tabular-nums;
      }

      .ui-pagination-controls {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 10px;
      }

      .ui-pagination-pages {
        display: inline-flex;
        align-items: center;
        gap: 4px;
      }

      .ui-page-btn {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 30px;
        height: 30px;
        box-sizing: border-box;
        border: 1px solid #e2e8f0;
        border-radius: 4px;
        background: #fff;
        color: #334155;
        cursor: pointer;
        font:
          500 12px/1 'Nunito Sans',
          Consolas,
          monospace;
        font-variant-numeric: tabular-nums;
        padding: 0 8px;

        &:hover:not(:disabled):not(.is-active) {
          border-color: #cbd5e1;
          background: #f8fafc;
          color: #0f172a;
        }

        &:focus-visible {
          outline: 2px solid #002b49;
          outline-offset: 1px;
        }

        &:disabled {
          cursor: not-allowed;
          opacity: 0.45;
        }

        &.is-active {
          border-color: #002b49;
          background: #002b49;
          color: #fff;
          font-weight: 600;
        }
      }

      .ui-page-ellipsis {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 24px;
        color: #94a3b8;
        font-size: 12px;
      }

      .ui-size-dropdown {
        min-width: 112px;
      }
    `,
  ],
})
export class TablePaginationComponent {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly totalCount = input.required<number>();
  readonly rangeTemplate = input<string>('{from}–{to} / {total}');
  readonly rangeText = input<string | null>(null);
  readonly pageSizeOptions = input<readonly number[]>([]);
  readonly pageSizeTemplate = input<string>('{size} / page');
  readonly ariaLabel = input<string>('Pagination');
  readonly pageSizeAriaLabel = input<string>('Rows per page');
  readonly alwaysShowPages = input<boolean>(true);
  readonly disabled = input<boolean>(false);

  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();

  protected readonly sizeSelectOptions = computed<SelectOption<number>[]>(() =>
    this.pageSizeOptions().map((size) => ({
      value: size,
      label: this.formatPageSize(size),
    })),
  );

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / Math.max(1, this.pageSize()))),
  );

  protected readonly summary = computed(() => {
    const override = this.rangeText();
    if (override !== null) return override;
    const total = this.totalCount();
    const from = total === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1;
    const to = Math.min(total, this.page() * this.pageSize());
    return this.rangeTemplate()
      .replace('{from}', String(from))
      .replace('{first}', String(from))
      .replace('{to}', String(to))
      .replace('{last}', String(to))
      .replace('{total}', String(total));
  });

  protected readonly pageItems = computed<Array<number | 'ellipsis'>>(() => {
    const total = this.pageCount();
    const current = Math.min(Math.max(1, this.page()), total);
    if (total <= 5) {
      return Array.from({ length: total }, (_, idx) => idx + 1);
    }
    const items: Array<number | 'ellipsis'> = [1];
    const start = Math.max(2, current - 1);
    const end = Math.min(total - 1, current + 1);
    if (start > 2) items.push('ellipsis');
    for (let p = start; p <= end; p++) items.push(p);
    if (end < total - 1) items.push('ellipsis');
    items.push(total);
    return items;
  });

  protected selectPage(target: number): void {
    if (this.disabled() || target < 1 || target > this.pageCount() || target === this.page()) {
      return;
    }
    this.pageChange.emit(target);
  }

  protected onSizeChange(size: number): void {
    if (!this.disabled() && size > 0 && size !== this.pageSize()) {
      this.pageSizeChange.emit(size);
    }
  }

  private formatPageSize(size: number): string {
    const tpl = this.pageSizeTemplate();
    return tpl.includes('{size}') ? tpl.replace('{size}', String(size)) : `${size}`;
  }
}
