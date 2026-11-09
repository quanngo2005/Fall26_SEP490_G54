import {
  Component,
  computed,
  DestroyRef,
  HostListener,
  inject,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, finalize, of, switchMap } from 'rxjs';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { RISK_EVENT_TYPES, RISK_SEVERITIES, RiskSeverity } from '../../core/risk-event.models';
import { RiskEventService } from '../../core/risk-event.service';
import {
  ALLOWED_FILE_EXTENSIONS,
  fileExtension,
  MAX_FILE_BYTES,
  occurredAtOf,
  riskErrorKey,
  todayInput,
} from './risk-event-display';

const MAX_PENALTY = 100;
const MAX_DESCRIPTION = 2000;

/** Modal of the "record risk event" button (SCR-RSK-01-M1). The event is recorded and penalised in one step. */
@Component({
  selector: 'app-risk-event-create-modal',
  imports: [FormsModule],
  templateUrl: './risk-event-create-modal.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/modal.scss',
    './risk-event-create-modal.component.scss',
  ],
})
export class RiskEventCreateModalComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(RiskEventService);
  private readonly employeesApi = inject(EmployeeService);
  private readonly destroyRef = inject(DestroyRef);

  readonly created = output<{ code: string; uploadFailed: boolean }>();
  readonly dismissed = output<void>();

  protected readonly types = RISK_EVENT_TYPES;
  protected readonly severities = RISK_SEVERITIES;
  protected readonly accept = ALLOWED_FILE_EXTENSIONS.join(',');
  protected readonly maxDate = todayInput();

  protected readonly employees = signal<EmployeeDetail[]>([]);
  protected readonly loadingEmployees = signal(true);
  protected readonly employeeId = signal('');
  protected readonly occurredOn = signal(todayInput());
  protected readonly eventType = signal('');
  protected readonly severity = signal<RiskSeverity>('MEDIUM');
  protected readonly penalty = signal<number | null>(null);
  protected readonly workCode = signal('');
  protected readonly description = signal('');
  protected readonly files = signal<File[]>([]);
  protected readonly fileErrors = signal<string[]>([]);
  protected readonly showValidation = signal(false);
  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);

  protected readonly employeeValid = computed(() => this.employeeId() !== '');
  protected readonly dateValid = computed(
    () => this.occurredOn() !== '' && this.occurredOn() <= this.maxDate,
  );
  protected readonly typeValid = computed(() => this.eventType() !== '');
  protected readonly penaltyValid = computed(() => {
    const points = this.penalty();
    return points !== null && points > 0 && points <= MAX_PENALTY;
  });
  protected readonly descriptionValid = computed(() => {
    const length = this.description().trim().length;
    return length > 0 && length <= MAX_DESCRIPTION;
  });
  protected readonly valid = computed(
    () =>
      this.employeeValid() &&
      this.dateValid() &&
      this.typeValid() &&
      this.penaltyValid() &&
      this.descriptionValid(),
  );

  ngOnInit(): void {
    this.employeesApi
      .list({ status: 'ACTIVE', pageSize: 100 })
      .pipe(
        catchError(() => of(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((page) => {
        this.employees.set(page?.items ?? []);
        this.loadingEmployees.set(false);
      });
  }

  protected typeLabel(code: string): string {
    return (this.localization.riskEvents.types as Record<string, string>)[code] ?? code;
  }

  protected severityLabel(severity: RiskSeverity): string {
    return this.localization.riskEvents.severity[severity];
  }

  protected onFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    const errors: string[] = [];
    const accepted = [...this.files()];
    for (const file of Array.from(input.files ?? [])) {
      if (!ALLOWED_FILE_EXTENSIONS.includes(fileExtension(file.name))) {
        errors.push(this.i18n.translate('riskEvents.form.errors.fileType', { name: file.name }));
      } else if (file.size > MAX_FILE_BYTES) {
        errors.push(this.i18n.translate('riskEvents.form.errors.fileSize', { name: file.name }));
      } else if (!accepted.some((f) => f.name === file.name && f.size === file.size)) {
        accepted.push(file);
      }
    }
    this.files.set(accepted);
    this.fileErrors.set(errors);
    input.value = '';
  }

  protected removeFile(file: File): void {
    this.files.update((list) => list.filter((f) => f !== file));
  }

  protected removeLabel(name: string): string {
    return this.i18n.translate('riskEvents.form.removeFile', { name });
  }

  protected submit(): void {
    this.showValidation.set(true);
    if (!this.valid() || this.submitting()) return;
    this.errorKey.set(null);
    this.submitting.set(true);
    const work = this.workCode().trim();
    const files = this.files();
    this.api
      .create({
        employeeId: this.employeeId(),
        eventType: this.eventType(),
        description: this.description().trim(),
        severity: this.severity(),
        occurredAt: occurredAtOf(this.occurredOn()),
        penaltyPoints: this.penalty() as number,
        ...(work ? { workCode: work } : {}),
        record: true,
      })
      .pipe(
        // The event is already recorded when the attachments go up: a failed upload is reported, never undone.
        switchMap((created) =>
          files.length === 0
            ? of({ code: created.code, uploadFailed: false })
            : this.api.uploadEvidence(created.id, files).pipe(
                switchMap(() => of({ code: created.code, uploadFailed: false })),
                catchError(() => of({ code: created.code, uploadFailed: true })),
              ),
        ),
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => this.created.emit(result),
        error: (error: unknown) => this.errorKey.set(riskErrorKey(error, 'create')),
      });
  }

  protected dismiss(): void {
    if (!this.submitting()) this.dismissed.emit();
  }

  protected errorText(): string {
    const key = this.errorKey();
    return key ? this.i18n.translate(key) : '';
  }

  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.dismiss();
    } else if (event.key.toLowerCase() === 's' && event.ctrlKey) {
      event.preventDefault();
      this.submit();
    }
  }
}
