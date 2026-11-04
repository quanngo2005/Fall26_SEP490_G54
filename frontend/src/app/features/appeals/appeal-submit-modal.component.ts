import { Component, DestroyRef, inject, OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, finalize, forkJoin, map, of, switchMap } from 'rxjs';
import { AppealService } from '../../core/appeal.service';
import { EmployeeService } from '../../core/employee.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { ResultSubmissionService } from '../../core/result-submission.service';
import { SubmissionDetail } from '../../core/result-submission.models';
import { WorkService } from '../../core/work.service';
import { appealErrorKey } from './appeal-display';

interface RejectedSubmission {
  id: string;
  workCode: string;
  workTitle: string;
  submissionNo: number;
}

/** Form of the "+ submit appeal" button: a result appeal about one of the caller's rejected submissions. */
@Component({
  selector: 'app-appeal-submit-modal',
  imports: [FormsModule],
  templateUrl: './appeal-submit-modal.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/modal.scss',
    './appeal-submit-modal.component.scss',
  ],
})
export class AppealSubmitModalComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(AppealService);
  private readonly works = inject(WorkService);
  private readonly submissions = inject(ResultSubmissionService);
  private readonly employees = inject(EmployeeService);
  private readonly destroyRef = inject(DestroyRef);

  readonly submitted = output<void>();
  readonly dismissed = output<void>();

  protected readonly options = signal<RejectedSubmission[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly selectedId = signal('');
  protected readonly reason = signal('');
  protected readonly files = signal<File[]>([]);
  protected readonly submitting = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly showValidation = signal(false);

  ngOnInit(): void {
    this.employees
      .me()
      .pipe(
        switchMap((me) =>
          this.works.list({ status: ['Rejected'], assigneeEmployeeId: me.id, pageSize: 100 }).pipe(
            switchMap((page) =>
              page.items.length === 0
                ? of([] as { workCode: string; workTitle: string; list: SubmissionDetail[] }[])
                : forkJoin(
                    page.items.map((work) =>
                      this.submissions
                        .getSubmissionsByWork(work.id)
                        .pipe(
                          map((list) => ({ workCode: work.code, workTitle: work.title, list })),
                        ),
                    ),
                  ),
            ),
            map((rows) => ({ rows, meId: me.id })),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ rows, meId }) => {
          this.options.set(
            rows.flatMap((row) => {
              // Only the latest submission of a work can still be appealed, and only when it is rejected.
              const latest = [...row.list]
                .filter((s) => s.submitterEmployeeId === meId)
                .sort((a, b) => b.submissionNo - a.submissionNo)[0];
              return latest && latest.status === 'REJECTED'
                ? [
                    {
                      id: latest.id,
                      workCode: row.workCode,
                      workTitle: row.workTitle,
                      submissionNo: latest.submissionNo,
                    },
                  ]
                : [];
            }),
          );
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  protected onFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.files.set(Array.from(input.files ?? []));
  }

  protected canSubmit(): boolean {
    return this.selectedId() !== '' && this.reason().trim().length > 0;
  }

  protected submit(): void {
    this.showValidation.set(true);
    if (!this.canSubmit() || this.submitting()) return;
    const form = new FormData();
    form.append('reason', this.reason().trim());
    for (const file of this.files()) form.append('files', file, file.name);
    this.submitting.set(true);
    this.errorKey.set(null);
    this.api
      .createForSubmission(this.selectedId(), form)
      .pipe(
        finalize(() => this.submitting.set(false)),
        catchError((error: unknown) => {
          this.errorKey.set(appealErrorKey(error));
          return of(null);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((created) => {
        if (created) this.submitted.emit();
      });
  }

  protected dismiss(): void {
    if (!this.submitting()) this.dismissed.emit();
  }

  protected errorText(): string {
    const key = this.errorKey();
    return key ? this.i18n.translate(key) : '';
  }
}
