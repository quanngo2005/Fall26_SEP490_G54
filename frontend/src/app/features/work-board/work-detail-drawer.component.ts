import { Component, DestroyRef, HostListener, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap, tap } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { WorkService } from '../../core/work.service';
import { WorkDetail } from '../../core/work.models';
import { dateOnly } from './work-board.utils';

/** Read-only detail drawer for one work (`GET /works/{id}`). */
@Component({
  selector: 'app-work-detail-drawer',
  standalone: true,
  templateUrl: './work-detail-drawer.component.html',
  styleUrl: './work-detail-drawer.component.scss',
})
export class WorkDetailDrawerComponent extends LocalizedComponent {
  private readonly works = inject(WorkService);
  private readonly destroyRef = inject(DestroyRef);

  readonly workId = input.required<string>();
  readonly closed = output<void>();

  protected readonly work = signal<WorkDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  constructor() {
    super();
    toObservable(this.workId)
      .pipe(
        tap(() => {
          this.work.set(null);
          this.loading.set(true);
          this.failed.set(false);
        }),
        switchMap((id) =>
          this.works.detail(id).pipe(
            catchError(() => {
              this.failed.set(true);
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((work) => {
        this.work.set(work);
        this.loading.set(false);
      });
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.closed.emit();
  }

  protected day(value: string | null): string {
    return value
      ? this.i18n.formatDate(dateOnly(value))
      : this.localization.department.notAvailable;
  }

  protected text(value: string | null | undefined): string {
    return value?.trim() ? value : this.localization.department.notAvailable;
  }
}
