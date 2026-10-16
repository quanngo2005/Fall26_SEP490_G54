import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
} from '@angular/core';

export interface FilterOption {
  readonly value: string;
  readonly label: string;
}

export interface FilterDefinition {
  readonly key: string;
  readonly label: string;
  readonly options: readonly FilterOption[];
  /** Label of the "no filter" entry, e.g. "Tất cả chức danh". */
  readonly allLabel: string;
}

export interface FilterChange {
  readonly key: string;
  readonly value: string;
}

const SEARCH_DEBOUNCE_MS = 250;

@Component({
  selector: 'app-filter-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bar" role="search">
      <label class="search">
        <span class="search__icon" aria-hidden="true">⌕</span>
        <input
          type="search"
          [placeholder]="searchPlaceholder()"
          [attr.aria-label]="searchPlaceholder()"
          [value]="searchValue()"
          (input)="onSearchInput($event)"
        />
      </label>
      @for (filter of filters(); track filter.key) {
        <select
          class="select"
          [attr.aria-label]="filter.label"
          [value]="filterValues()[filter.key] ?? ''"
          (change)="onFilterChange(filter.key, $event)"
        >
          <option value="">{{ filter.allLabel }}</option>
          @for (option of filter.options; track option.value) {
            <option [value]="option.value" [selected]="filterValues()[filter.key] === option.value">
              {{ option.label }}
            </option>
          }
        </select>
      }
      @if (hasActiveFilters()) {
        <button type="button" class="reset" (click)="onReset()">Xóa bộ lọc</button>
      }
      <div class="extra"><ng-content /></div>
    </div>
  `,
  styleUrl: './filter-bar.component.scss',
})
export class FilterBarComponent {
  readonly searchPlaceholder = input('Tìm kiếm…');
  readonly searchValue = input('');
  readonly filters = input<readonly FilterDefinition[]>([]);
  readonly filterValues = input<Readonly<Record<string, string>>>({});

  readonly searchChange = output<string>();
  readonly filterChange = output<FilterChange>();
  readonly filtersReset = output<void>();

  protected readonly hasActiveFilters = computed(
    () => this.searchValue() !== '' || Object.values(this.filterValues()).some((v) => v !== ''),
  );

  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.searchTimer));
  }

  protected onSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.searchChange.emit(value.trim()), SEARCH_DEBOUNCE_MS);
  }

  protected onFilterChange(key: string, event: Event): void {
    this.filterChange.emit({ key, value: (event.target as HTMLSelectElement).value });
  }

  protected onReset(): void {
    clearTimeout(this.searchTimer);
    this.filtersReset.emit();
  }
}
