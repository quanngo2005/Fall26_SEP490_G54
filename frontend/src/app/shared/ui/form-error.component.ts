import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { AbstractControl, ValidationErrors } from '@angular/forms';

const DEFAULT_MESSAGES: Record<string, (error: unknown) => string> = {
  required: () => 'Trường này là bắt buộc.',
  email: () => 'Email không hợp lệ.',
  minlength: (error) => `Tối thiểu ${(error as { requiredLength: number }).requiredLength} ký tự.`,
  maxlength: (error) => `Tối đa ${(error as { requiredLength: number }).requiredLength} ký tự.`,
  min: (error) => `Giá trị tối thiểu là ${(error as { min: number }).min}.`,
  max: (error) => `Giá trị tối đa là ${(error as { max: number }).max}.`,
  pattern: () => 'Định dạng không hợp lệ.',
};

/**
 * Displays validation feedback below a form field. Client-side errors come from an
 * `AbstractControl` (shown once touched/dirty); server-side errors are passed via `serverErrors`.
 */
@Component({
  selector: 'app-form-error',
  // Default change detection is required: AbstractControl state (touched/errors) mutates
  // outside signal inputs, so OnPush would show stale validation messages.
  // eslint-disable-next-line @angular-eslint/prefer-on-push-component-change-detection
  changeDetection: ChangeDetectionStrategy.Default,
  template: `
    @if (messages().length > 0) {
      <ul class="errors" role="alert">
        @for (message of messages(); track message) {
          <li class="error-item">
            <span class="error-icon" aria-hidden="true">⚠</span>
            <span>{{ message }}</span>
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './form-error.component.scss',
})
export class FormErrorComponent {
  readonly control = input<AbstractControl | null>(null);
  /** Overrides keyed by validator name, e.g. `{ required: 'Vui lòng nhập tên.' }`. */
  readonly customMessages = input<Record<string, string>>({});
  readonly serverErrors = input<readonly string[]>([]);

  protected readonly serverMessages = computed(() => this.serverErrors());

  protected messages(): readonly string[] {
    const result: string[] = [...this.serverMessages()];
    const control = this.control();
    if (control && control.invalid && (control.touched || control.dirty)) {
      result.push(...this.clientMessages(control.errors));
    }
    return result;
  }

  private clientMessages(errors: ValidationErrors | null): string[] {
    if (!errors) {
      return [];
    }
    const custom = this.customMessages();
    return Object.entries(errors).map(
      ([key, detail]) => custom[key] ?? DEFAULT_MESSAGES[key]?.(detail) ?? 'Giá trị không hợp lệ.',
    );
  }
}
