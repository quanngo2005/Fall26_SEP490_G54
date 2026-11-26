import { Component, inject, input, output, signal } from '@angular/core';
import {
  CustomerImportAction,
  CustomerImportError,
  CustomerImportPreview,
  CustomerImportResult,
  IMPORT_COLUMNS,
} from '../../core/campaign-customer.models';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { CampaignDetail } from '../../core/campaign.models';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { AppNumberPipe } from '../../shared/pipes';
import { saveFile } from '../performance-dashboard/performance-page';
import { apiErrorCode, fillTemplate, serverMessage } from './campaign.utils';

type Step = 'CHOOSE_FILE' | 'PREVIEW' | 'RESULT';

/** Same cap as the controller's request-size limit; the configured limit is enforced by the API. */
const MAX_FILE_BYTES = 10 * 1024 * 1024;

/** SCR-CMP-01-M3: choose an .xlsx, preview every row, then commit the valid ones. */
@Component({
  selector: 'app-campaign-customer-import-modal',
  imports: [AppNumberPipe],
  templateUrl: './campaign-customer-import-modal.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.part2.scss',
    '../../shared/ui/modal.scss',
    './campaign-customer-import-modal.component.scss',
  ],
})
export class CampaignCustomerImportModalComponent extends LocalizedComponent {
  private readonly api = inject(CampaignCustomerService);

  readonly campaign = input.required<CampaignDetail>();
  readonly closed = output<void>();
  readonly committed = output<void>();

  protected readonly columns = IMPORT_COLUMNS;
  protected readonly step = signal<Step>('CHOOSE_FILE');
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly preview = signal<CustomerImportPreview | null>(null);
  protected readonly result = signal<CustomerImportResult | null>(null);
  protected readonly skipErrors = signal(true);

  protected get text() {
    return this.localization.campaignCustomers.importModal;
  }

  protected onFileChosen(event: Event): void {
    const input = event.target as HTMLInputElement;
    const chosen = input.files?.[0] ?? null;
    this.fileError.set(null);
    this.loadError.set(null);
    if (!chosen) {
      this.file.set(null);
      return;
    }
    if (!chosen.name.toLowerCase().endsWith('.xlsx')) {
      this.reject(input, this.text.errorExtension);
      return;
    }
    if (chosen.size > MAX_FILE_BYTES) {
      this.reject(input, this.text.errorTooLarge);
      return;
    }
    this.file.set(chosen);
  }

  protected removeFile(input: HTMLInputElement): void {
    this.file.set(null);
    input.value = '';
  }

  protected downloadTemplate(): void {
    this.api.downloadTemplate(this.campaign().id).subscribe({
      next: (blob) => saveFile(blob, 'campaign-customers-template.xlsx'),
      error: (error: unknown) => this.loadError.set(this.failureText(error)),
    });
  }

  protected submitPreview(): void {
    const file = this.file();
    if (!file) {
      return;
    }
    this.loading.set(true);
    this.loadError.set(null);
    this.api.importPreview(this.campaign().id, file).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        this.skipErrors.set(true);
        this.loading.set(false);
        this.step.set('PREVIEW');
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(this.failureText(error));
      },
    });
  }

  protected backToChooseFile(): void {
    this.step.set('CHOOSE_FILE');
  }

  protected canCommit(): boolean {
    const preview = this.preview();
    if (!preview || this.loading() || preview.summary.valid === 0) {
      return false;
    }
    return preview.summary.invalid === 0 || this.skipErrors();
  }

  protected commit(): void {
    const file = this.file();
    if (!file || !this.canCommit()) {
      return;
    }
    this.loading.set(true);
    this.loadError.set(null);
    this.api.importCommit(this.campaign().id, file).subscribe({
      next: (result) => {
        this.result.set(result);
        this.loading.set(false);
        this.step.set('RESULT');
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(this.failureText(error));
      },
    });
  }

  protected close(): void {
    if (this.result()) {
      this.committed.emit();
    } else {
      this.closed.emit();
    }
  }

  protected actionLabel(action: CustomerImportAction): string {
    switch (action) {
      case 'CREATE':
        return this.text.actionCreate;
      case 'UPDATE':
        return this.text.actionUpdate;
      default:
        return this.text.actionInvalid;
    }
  }

  protected actionBadge(action: CustomerImportAction): string {
    return action === 'INVALID' ? 'is-danger' : action === 'UPDATE' ? 'is-warning' : 'is-info';
  }

  protected errorLine(rowNumber: number, error: CustomerImportError): string {
    return fillTemplate(this.text.errorAt, {
      row: rowNumber,
      column: error.column,
      message: error.message,
    });
  }

  protected skipLabel(preview: CustomerImportPreview): string {
    return fillTemplate(this.text.skipErrors, {
      invalid: preview.summary.invalid,
      valid: preview.summary.valid,
    });
  }

  protected commitLabel(preview: CustomerImportPreview): string {
    return fillTemplate(this.text.commit, { count: preview.summary.valid });
  }

  protected resultText(result: CustomerImportResult): string {
    return fillTemplate(this.text.resultSummary, {
      created: result.created,
      updated: result.updated,
      skipped: result.skipped,
    });
  }

  private reject(input: HTMLInputElement, message: string): void {
    this.fileError.set(message);
    this.file.set(null);
    input.value = '';
  }

  private failureText(error: unknown): string {
    const messages = this.localization.campaignCustomers.errors as Record<string, string>;
    const code = apiErrorCode(error);
    return (code && messages[code]) || serverMessage(error) || this.text.failed;
  }
}
