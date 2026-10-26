import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, EMPTY, finalize, map, Subject, switchMap, tap } from 'rxjs';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { WorkTypeDetail, WorkTypeKpiDetail, WorkTypeStatus } from '../../core/work-type.models';
import { WorkTypeService } from '../../core/work-type.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';

type MappingState = 'current' | 'upcoming' | 'expired';

@Component({
  selector: 'app-work-type-detail',
  imports: [FormsModule, HeaderComponent, SidebarComponent, LayoutIconComponent],
  templateUrl: './work-type-detail.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/workspace-page.scss',
    '../../shared/ui/workspace-page.part2.scss',
    './work-type-detail.component.scss',
  ],
})
export class WorkTypeDetailComponent extends LocalizedComponent {
  private readonly api = inject(WorkTypeService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  protected readonly workTypes = signal<WorkTypeDetail[]>([]);
  protected readonly workType = signal<WorkTypeDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly includeHistory = signal(true);
  protected selectedId = '';

  constructor() {
    super();
    this.reload
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.failed.set(false);
          return this.api.list({ pageSize: 100 }).pipe(
            tap((page) => {
              this.workTypes.set(page.items);
              const requested = this.route.snapshot.paramMap.get('id');
              const current = page.items.find((item) => item.id === requested) ?? page.items[0];
              this.workType.set(current ?? null);
              this.selectedId = current?.id ?? '';
            }),
            catchError(() => {
              this.failed.set(true);
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    this.route.paramMap
      .pipe(
        map((params) => params.get('id')),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((id) => {
        const known = this.workTypes().find((item) => item.id === id);
        if (known) {
          this.workType.set(known);
          this.selectedId = known.id;
        } else {
          this.reload.next();
        }
      });
  }

  protected refresh(): void {
    this.reload.next();
  }

  protected changeWorkType(): void {
    void this.router.navigate(['/work-type-detail', this.selectedId]);
  }

  protected toggleHistory(event: Event): void {
    this.includeHistory.set((event.target as HTMLInputElement).checked);
  }

  protected requiredCount(workType: WorkTypeDetail): number {
    return workType.kpis.filter((kpi) => kpi.isRequired).length;
  }

  protected visibleKpis(workType: WorkTypeDetail): WorkTypeKpiDetail[] {
    return this.includeHistory()
      ? workType.kpis
      : workType.kpis.filter((kpi) => this.mappingState(kpi) !== 'expired');
  }

  /** Derived from the date-only range against today's UTC date (docs/api/WORK_TYPE_CONTRACT.md). */
  protected mappingState(kpi: WorkTypeKpiDetail): MappingState {
    const today = new Date().toISOString().slice(0, 10);
    if (kpi.effectiveFrom > today) return 'upcoming';
    if (kpi.effectiveTo && kpi.effectiveTo < today) return 'expired';
    return 'current';
  }

  protected mappingLabel(kpi: WorkTypeKpiDetail): string {
    const text = this.localization.workType;
    const state = this.mappingState(kpi);
    return state === 'current' ? text.current : state === 'upcoming' ? text.upcoming : text.expired;
  }

  protected statusLabel(status: WorkTypeStatus | null): string {
    const text = this.localization.workType;
    switch (status) {
      case 'ACTIVE':
        return text.statusActive;
      case 'INACTIVE':
        return text.statusInactive;
      case 'ARCHIVED':
        return text.statusArchived;
      default:
        return text.notAvailable;
    }
  }
}

export default WorkTypeDetailComponent;
