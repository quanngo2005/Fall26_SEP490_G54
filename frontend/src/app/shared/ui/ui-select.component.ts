import {
  Component,
  computed,
  ElementRef,
  forwardRef,
  HostListener,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export interface SelectOption<T = string> {
  readonly value: T;
  readonly label: string;
  readonly sublabel?: string;
  readonly disabled?: boolean;
}

let nextSelectId = 0;

/**
 * Custom styled enterprise dropdown replacing native Windows `<select>` popups.
 * Follows DESIGN.md: 8px rounded floating panel, active/dropdown elevation shadow,
 * keyboard navigation (ArrowUp/Down, Home/End, Enter/Space, Escape), and checkmark active indicator.
 */
@Component({
  selector: 'app-ui-select',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => UiSelectComponent),
      multi: true,
    },
  ],
  template: `
    <div
  class="ui-select-root"
  [class.is-open]="open()"
  [class.is-compact]="size() === 'sm'"
  [class.is-disabled]="isDisabled()"
>
  <button
    #triggerBtn
    type="button"
    class="ui-select-trigger"
    role="combobox"
    [attr.id]="triggerId() || null"
    [attr.aria-controls]="listboxId"
    [attr.aria-expanded]="open()"
    aria-haspopup="listbox"
    [attr.aria-label]="ariaLabel() || null"
    [disabled]="isDisabled()"
    (click)="toggle()"
    (keydown)="onTriggerKeydown($event)"
  >
    @if (prefix()) {
      <span class="ui-select-prefix">{{prefix()}}</span>
    }
    <span class="ui-select-value" [class.is-placeholder]="isPlaceholder()">
      {{selectedLabel()}}
    </span>
    <svg
      class="ui-select-chevron"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="2.2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <path d="m6 9 6 6 6-6" />
    </svg>
  </button>

  @if (open()) {
    <ul
      #menuEl
      [id]="listboxId"
      class="ui-select-menu select-menu"
      [class.is-drop-up]="dropUp()"
      role="listbox"
      tabindex="-1"
      [attr.aria-label]="ariaLabel() || null"
      (keydown)="onMenuKeydown($event)"
    >
      @for (opt of options(); track opt.value; let idx = $index) {
        <li
          role="option"
          tabindex="-1"
          class="ui-select-option"
          [class.is-selected]="opt.value === currentValue()"
          [class.is-focused]="idx === activeIndex()"
          [class.is-disabled]="opt.disabled"
          [attr.aria-selected]="opt.value === currentValue()"
          (mouseenter)="setActiveIndex(idx)"
          (mousedown)="$event.preventDefault()"
          (click)="choose(opt)"
          (keydown.enter)="choose(opt)"
        >
          <span class="ui-option-text">
            <span class="ui-option-label">{{opt.label}}</span>
            @if (opt.sublabel) {
              <span class="ui-option-sub">{{opt.sublabel}}</span>
            }
          </span>
          @if (opt.value === currentValue()) {
            <svg
              class="ui-option-check"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2.5"
              stroke-linecap="round"
              stroke-linejoin="round"
              aria-hidden="true"
            >
              <path d="M20 6 9 17l-5-5" />
            </svg>
          }
        </li>
      }
    </ul>
  }
</div>
  `,
  styles: [
    `
      :host {
        display: inline-block;
        min-width: 156px;
        vertical-align: middle;
      }

      .ui-select-root {
        position: relative;
        width: 100%;
      }

      .ui-select-trigger {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        width: 100%;
        height: 38px;
        box-sizing: border-box;
        border: 1px solid #cbd5e1;
        border-radius: 4px;
        background: #fff;
        color: #1e293b;
        cursor: pointer;
        font:
          400 13px/18px 'Nunito Sans',
          sans-serif;
        padding: 0 10px 0 12px;
        text-align: left;
        transition:
          border-color 150ms ease,
          box-shadow 150ms ease,
          background-color 150ms ease;

        &:hover:not(:disabled) {
          border-color: #94a3b8;
        }

        &:focus-visible {
          border-color: #002b49;
          box-shadow: 0 0 0 3px rgb(0 43 73 / 10%);
          outline: none;
        }

        &:disabled {
          cursor: not-allowed;
          background: #f8fafc;
          color: #94a3b8;
          opacity: 0.7;
        }
      }

      .ui-select-root.is-open .ui-select-trigger {
        border-color: #002b49;
        box-shadow: 0 0 0 3px rgb(0 43 73 / 10%);
      }

      .ui-select-root.is-compact .ui-select-trigger {
        height: 30px;
        font-size: 12px;
        font-weight: 500;
        padding: 0 8px 0 10px;
      }

      .ui-select-value {
        flex: 1 1 auto;
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;

        &.is-placeholder {
          color: #475569;
        }
      }

      .ui-select-prefix {
        flex: 0 0 auto;
        color: #64748b;
        font-weight: 400;
      }

      .ui-select-prefix + .ui-select-value {
        color: #0f172a;
        font-weight: 600;
      }

      .ui-select-chevron {
        flex: 0 0 auto;
        width: 15px;
        height: 15px;
        color: #64748b;
        transition: transform 180ms cubic-bezier(0.22, 1, 0.36, 1);
      }

      .ui-select-root.is-open .ui-select-chevron {
        color: #002b49;
        transform: rotate(180deg);
      }

      .ui-select-menu {
        position: absolute;
        top: calc(100% + 5px);
        left: 0;
        z-index: 120;
        min-width: 100%;
        width: max-content;
        max-width: min(320px, 90vw);
        max-height: 264px;
        box-sizing: border-box;
        margin: 0;
        padding: 5px;
        list-style: none;
        overflow-y: auto;
        border: 1px solid #e2e8f0;
        border-radius: 8px;
        background: #fff;
        box-shadow:
          0 10px 24px -4px rgb(15 23 42 / 12%),
          0 4px 8px -2px rgb(15 23 42 / 6%);
        outline: none;

        &.is-drop-up {
          top: auto;
          bottom: calc(100% + 5px);
          transform-origin: bottom center;
        }
      }

      .ui-select-menu::-webkit-scrollbar {
        width: 6px;
      }

      .ui-select-menu::-webkit-scrollbar-thumb {
        border-radius: 9999px;
        background: #cbd5e1;
      }

      .ui-select-option {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 10px;
        min-height: 34px;
        box-sizing: border-box;
        border-radius: 5px;
        color: #1e293b;
        cursor: pointer;
        font:
          400 13px/18px 'Nunito Sans',
          sans-serif;
        padding: 6px 10px;
        transition:
          background-color 100ms ease,
          color 100ms ease;

        &.is-focused:not(.is-disabled) {
          background: #f1f5f9;
          color: #0f172a;
        }

        &.is-selected {
          background: #eff6ff;
          color: #002b49;
          font-weight: 600;
        }

        &.is-selected.is-focused {
          background: #dbeafe;
        }

        &.is-disabled {
          color: #94a3b8;
          cursor: not-allowed;
        }
      }

      .ui-select-root.is-compact .ui-select-option {
        min-height: 30px;
        font-size: 12px;
        padding: 4px 9px;
      }

      .ui-option-text {
        display: flex;
        align-items: baseline;
        gap: 6px;
        min-width: 0;
        overflow: hidden;
      }

      .ui-option-label {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .ui-option-sub {
        color: #64748b;
        font-size: 11.5px;
        font-weight: 400;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .ui-option-check {
        flex: 0 0 auto;
        width: 14px;
        height: 14px;
        color: #002b49;
      }
    `,
  ],
})
export class UiSelectComponent<T = string> implements ControlValueAccessor {
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly triggerBtn = viewChild<ElementRef<HTMLButtonElement>>('triggerBtn');
  private readonly menuEl = viewChild<ElementRef<HTMLUListElement>>('menuEl');

  protected readonly listboxId = `ui-select-list-${++nextSelectId}`;

  readonly options = input.required<readonly SelectOption<T>[]>();
  readonly placeholder = input<string>('');
  readonly ariaLabel = input<string>('');
  /** Muted label shown inside the trigger before the value ("Kỳ  Tháng 10/2026"). */
  readonly prefix = input<string>('');
  readonly triggerId = input<string>('');
  readonly size = input<'md' | 'sm'>('md');
  readonly disabled = input<boolean>(false);
  readonly value = input<T | undefined>(undefined);

  readonly valueChange = output<T>();

  protected readonly cvaValue = signal<T | undefined>(undefined);
  protected readonly cvaDisabled = signal<boolean>(false);
  protected readonly open = signal<boolean>(false);
  protected readonly dropUp = signal<boolean>(false);
  protected readonly activeIndex = signal<number>(-1);

  private onChange: (val: T) => void = () => {};
  private onTouched: () => void = () => {};

  protected readonly currentValue = computed<T | undefined>(() => {
    const direct = this.value();
    return direct !== undefined ? direct : this.cvaValue();
  });

  protected readonly isDisabled = computed(() => this.disabled() || this.cvaDisabled());

  protected readonly selectedOption = computed(() =>
    this.options().find((o) => o.value === this.currentValue()),
  );

  protected readonly selectedLabel = computed(
    () => this.selectedOption()?.label ?? this.placeholder(),
  );

  protected readonly isPlaceholder = computed(() => {
    const opt = this.selectedOption();
    return !opt || opt.value === ('' as unknown as T);
  });

  writeValue(val: T): void {
    this.cvaValue.set(val);
  }

  registerOnChange(fn: (val: T) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.cvaDisabled.set(isDisabled);
  }

  @HostListener('document:mousedown', ['$event'])
  protected onOutsideClick(event: MouseEvent): void {
    if (!this.open()) return;
    const target = event.target as Node | null;
    if (target && !this.host.nativeElement.contains(target)) {
      this.close(false);
    }
  }

  protected toggle(): void {
    if (this.isDisabled()) return;
    if (this.open()) {
      this.close(true);
    } else {
      this.openMenu();
    }
  }

  protected openMenu(): void {
    if (this.isDisabled() || this.options().length === 0) return;
    const rect = this.host.nativeElement.getBoundingClientRect();
    const spaceBelow = window.innerHeight - rect.bottom;
    this.dropUp.set(spaceBelow < 240 && rect.top > spaceBelow);

    const currentIdx = this.options().findIndex(
      (o) => o.value === this.currentValue() && !o.disabled,
    );
    const firstEnabled = this.options().findIndex((o) => !o.disabled);
    this.activeIndex.set(currentIdx >= 0 ? currentIdx : firstEnabled);
    this.open.set(true);

    queueMicrotask(() => {
      this.menuEl()?.nativeElement.focus({ preventScroll: true });
      this.scrollActiveIntoView();
    });
  }

  protected close(restoreFocus: boolean): void {
    if (!this.open()) return;
    this.open.set(false);
    this.onTouched();
    if (restoreFocus) {
      this.triggerBtn()?.nativeElement.focus({ preventScroll: true });
    }
  }

  protected setActiveIndex(index: number): void {
    if (!this.options()[index]?.disabled) {
      this.activeIndex.set(index);
    }
  }

  protected choose(option: SelectOption<T>): void {
    if (option.disabled) return;
    const changed = option.value !== this.currentValue();
    this.cvaValue.set(option.value);
    if (changed) {
      this.onChange(option.value);
      this.valueChange.emit(option.value);
    }
    this.close(true);
  }

  protected onTriggerKeydown(event: KeyboardEvent): void {
    if (this.isDisabled()) return;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      this.openMenu();
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.toggle();
    }
  }

  protected onMenuKeydown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'Escape':
        event.preventDefault();
        event.stopPropagation();
        this.close(true);
        break;
      case 'Tab':
        this.close(false);
        break;
      case 'ArrowDown':
        event.preventDefault();
        this.moveActive(1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.moveActive(-1);
        break;
      case 'Home':
        event.preventDefault();
        this.jumpToEdge(1);
        break;
      case 'End':
        event.preventDefault();
        this.jumpToEdge(-1);
        break;
      case 'Enter':
      case ' ': {
        event.preventDefault();
        const opt = this.options()[this.activeIndex()];
        if (opt && !opt.disabled) this.choose(opt);
        break;
      }
    }
  }

  private moveActive(delta: 1 | -1): void {
    const list = this.options();
    if (!list.length) return;
    let idx = this.activeIndex();
    for (let step = 0; step < list.length; step++) {
      idx = (idx + delta + list.length) % list.length;
      if (!list[idx].disabled) {
        this.activeIndex.set(idx);
        this.scrollActiveIntoView();
        return;
      }
    }
  }

  private jumpToEdge(direction: 1 | -1): void {
    const list = this.options();
    const indices =
      direction === 1 ? list.map((_, i) => i) : list.map((_, i) => list.length - 1 - i);
    const target = indices.find((i) => !list[i].disabled);
    if (target !== undefined) {
      this.activeIndex.set(target);
      this.scrollActiveIntoView();
    }
  }

  private scrollActiveIntoView(): void {
    const menu = this.menuEl()?.nativeElement;
    const item = menu?.children.item(this.activeIndex()) as HTMLElement | null;
    item?.scrollIntoView({ block: 'nearest' });
  }
}
