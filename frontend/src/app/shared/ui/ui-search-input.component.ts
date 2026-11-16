import { Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LayoutIconComponent } from '../layout/layout-icon.component';

/**
 * Shared search input with leading search icon and optional clear button.
 * Matches the User Accounts filter bar across every list screen.
 */
@Component({
  selector: 'app-ui-search-input',
  imports: [FormsModule, LayoutIconComponent],
  template: `
    <div class="ui-search-wrap" [class.is-disabled]="disabled()">
      <app-layout-icon name="search" class="ui-search-icon" />
      <input
        type="search"
        class="ui-search-el"
        [class.has-clear]="!!value() && !disabled()"
        [ngModel]="value()"
        [disabled]="disabled()"
        [placeholder]="placeholder()"
        [attr.aria-label]="ariaLabel() || placeholder()"
        (ngModelChange)="valueChange.emit($event)"
        (keydown.enter)="enterPressed.emit()"
      />
      @if (value() && !disabled()) {
        <button
          type="button"
          class="ui-search-clear"
          [attr.aria-label]="clearLabel()"
          (click)="clear()"
        >
          ×
        </button>
      }
    </div>
  `,
  styles: [
    `
      :host {
        display: inline-block;
        flex: 1 1 240px;
        max-width: 320px;
        min-width: 0;
      }

      .ui-search-wrap {
        position: relative;
        width: 100%;
      }

      .ui-search-icon {
        position: absolute;
        left: 11px;
        top: 50%;
        width: 16px;
        height: 16px;
        transform: translateY(-50%);
        color: #94a3b8;
        pointer-events: none;
      }

      .ui-search-el {
        width: 100%;
        height: 38px;
        box-sizing: border-box;
        border: 1px solid #cbd5e1;
        border-radius: 4px;
        background: #fff;
        color: #1e293b;
        font:
          400 13px/18px 'Nunito Sans',
          sans-serif;
        padding: 0 12px 0 34px;
        transition:
          border-color 150ms ease,
          box-shadow 150ms ease;

        &::placeholder {
          color: #64748b;
        }

        &:hover:not(:disabled) {
          border-color: #94a3b8;
        }

        &:focus {
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

        &::-webkit-search-cancel-button {
          display: none;
        }

        &.has-clear {
          padding-right: 32px;
        }
      }

      .ui-search-clear {
        position: absolute;
        top: 50%;
        right: 6px;
        display: grid;
        place-items: center;
        width: 24px;
        height: 24px;
        transform: translateY(-50%);
        border: 0;
        border-radius: 4px;
        background: transparent;
        color: #94a3b8;
        cursor: pointer;
        font-size: 16px;
        line-height: 1;
        padding: 0;

        &:hover {
          background: #f1f5f9;
          color: #475569;
        }

        &:focus-visible {
          outline: 2px solid #002b49;
          outline-offset: 1px;
        }
      }
    `,
  ],
})
export class UiSearchInputComponent {
  readonly value = input<string>('');
  readonly placeholder = input<string>('');
  readonly ariaLabel = input<string>('');
  readonly clearLabel = input<string>('Clear search');
  readonly disabled = input<boolean>(false);

  readonly valueChange = output<string>();
  readonly enterPressed = output<void>();
  readonly cleared = output<void>();

  protected clear(): void {
    this.valueChange.emit('');
    this.cleared.emit();
  }
}
