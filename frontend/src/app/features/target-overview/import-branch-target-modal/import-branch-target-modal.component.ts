import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, inject, Input, Output, signal } from '@angular/core';
import { ApiResponse } from '../../../core/api-response.model';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import {
  TargetImportCommitResult,
  TargetImportPreview,
} from '../../../core/target-assignment.models';
import { TargetAssignmentService } from '../../../core/target-assignment.service';
import { AppNumberPipe } from '../../../shared/pipes';

type Step = 'CHOOSE_FILE' | 'PREVIEW' | 'RESULT';

const MAX_FILE_BYTES = 10 * 1024 * 1024;

/**
 * SCR-STRAT-04-M1 (Figma `2928:2268`): 3-step import wizard - choose file, preview, result.
 * Opened from `BranchTargetOverviewComponent`'s "Thiết lập mục tiêu năm" button.
 */
@Component({
  selector: 'app-import-branch-target-modal',
  imports: [AppNumberPipe],
  templateUrl: './import-branch-target-modal.component.html',
  styleUrls: [
    '../../../shared/ui/ui-controls.scss',
    '../../../shared/ui/workspace-page.part2.scss',
    '../../../shared/ui/modal.scss',
    './import-branch-target-modal.component.scss',
  ],
})
export class ImportBranchTargetModalComponent extends LocalizedComponent {
  private readonly targetApi = inject(TargetAssignmentService);

  @Input({ required: true }) branchId!: string;
  @Input({ required: true }) year!: number;
  @Output() readonly closed = new EventEmitter<void>();
  @Output() readonly committed = new EventEmitter<void>();

  protected readonly step = signal<Step>('CHOOSE_FILE');
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly preview = signal<TargetImportPreview | null>(null);
  protected readonly result = signal<TargetImportCommitResult | null>(null);

  protected get localizedErrors(): Record<string, string> {
    return this.localization.importBranchTargetModal.errors as unknown as Record<string, string>;
  }

  protected errorMessage(code: string): string {
    return this.localizedErrors[code] ?? code;
  }

  protected resultSummaryText(data: TargetImportCommitResult): string {
    const total = data.committed + data.skipped;
    return this.localization.importBranchTargetModal.resultSummary
      .replace('{committed}', `${data.committed}`)
      .replace('{total}', `${total}`)
      .replace('{skipped}', `${data.skipped}`);
  }

  protected actionLabel(action: string): string {
    const text = this.localization.importBranchTargetModal;
    switch (action) {
      case 'CREATE':
        return text.actionCreate;
      case 'UPDATE':
        return text.actionUpdate;
      case 'UNCHANGED':
        return text.actionUnchanged;
      default:
        return text.actionInvalid;
    }
  }

  protected actionClass(action: string): string {
    return `badge is-${action.toLowerCase()}`;
  }

  protected commitButtonText(validCount: number): string {
    return this.localization.importBranchTargetModal.commit.replace('{count}', `${validCount}`);
  }

  protected onFileChosen(event: Event): void {
    const input = event.target as HTMLInputElement;
    const chosen = input.files?.[0] ?? null;
    this.fileError.set(null);

    if (!chosen) {
      this.file.set(null);
      return;
    }

    if (!chosen.name.toLowerCase().endsWith('.xlsx')) {
      this.fileError.set(this.localization.importBranchTargetModal.errorInvalidExtension);
      this.file.set(null);
      input.value = '';
      return;
    }

    if (chosen.size > MAX_FILE_BYTES) {
      this.fileError.set(this.localization.importBranchTargetModal.errorFileTooLarge);
      this.file.set(null);
      input.value = '';
      return;
    }

    this.file.set(chosen);
  }

  protected removeFile(input: HTMLInputElement): void {
    this.file.set(null);
    input.value = '';
  }

  protected downloadTemplate(): void {
    this.targetApi.downloadTemplate(this.year).subscribe((blob) => {
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `KPI-Nam-Quy-${this.year}-mau.xlsx`;
      link.click();
      URL.revokeObjectURL(url);
    });
  }

  protected submitPreview(): void {
    const file = this.file();
    if (!file) {
      return;
    }

    this.loading.set(true);
    this.loadError.set(null);
    this.targetApi.importPreview(this.branchId, this.year, file).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        this.loading.set(false);
        this.step.set('PREVIEW');
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.loadError.set(this.apiErrorMessage(error));
      },
    });
  }

  protected backToChooseFile(): void {
    this.step.set('CHOOSE_FILE');
  }

  protected commit(): void {
    const file = this.file();
    if (!file) {
      return;
    }

    this.loading.set(true);
    this.loadError.set(null);
    this.targetApi.importCommit(this.branchId, this.year, file).subscribe({
      next: (result) => {
        this.result.set(result);
        this.loading.set(false);
        this.step.set('RESULT');
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.loadError.set(this.apiErrorMessage(error));
      },
    });
  }

  protected close(): void {
    if (this.result()) {
      this.committed.emit();
    }

    this.closed.emit();
  }

  private apiErrorMessage(error: HttpErrorResponse): string {
    const body = error.error as ApiResponse<unknown> | undefined;
    const code = body?.errorCode;
    if (code && this.localizedErrors[code]) {
      return this.localizedErrors[code];
    }
    return this.step() === 'PREVIEW' || this.step() === 'RESULT'
      ? this.localization.importBranchTargetModal.commitFailed
      : this.localization.importBranchTargetModal.loadFailed;
  }
}
