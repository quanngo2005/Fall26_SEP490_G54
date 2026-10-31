import { Component, DestroyRef, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { catchError, debounceTime, EMPTY, Subject, switchMap } from 'rxjs';
import { CampaignObjective } from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { WorkTypeDetail } from '../../core/work-type.models';
import { WorkTypeService } from '../../core/work-type.service';
import { serverMessage } from './campaign.utils';

const SEARCH_DEBOUNCE_MS = 250;
const MATCH_LIMIT = 8;

@Component({
  selector: 'app-campaign-create-modal',
  imports: [FormsModule],
  templateUrl: './campaign-create-modal.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/modal.scss',
    './campaign-modal.scss',
  ],
})
export class CampaignCreateModalComponent extends LocalizedComponent {
  private readonly campaigns = inject(CampaignService);
  private readonly employees = inject(EmployeeService);
  private readonly workTypeApi = inject(WorkTypeService);
  private readonly employeeSearch = new Subject<string>();

  readonly closed = output<void>();
  /** Emits the id of the newly created campaign. */
  readonly created = output<string>();

  protected readonly objectives = signal<CampaignObjective[]>([]);
  protected readonly workTypes = signal<WorkTypeDetail[]>([]);
  protected readonly matches = signal<EmployeeDetail[]>([]);
  protected readonly participants = signal<EmployeeDetail[]>([]);
  protected readonly workTypeIds = signal<string[]>([]);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected code = '';
  protected name = '';
  protected objectiveId = '';
  protected startDate = '';
  protected endDate = '';
  protected description = '';
  protected keyword = '';

  constructor() {
    super();
    const destroyRef = inject(DestroyRef);
    this.campaigns
      .objectives()
      .pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe((objectives) => this.objectives.set(objectives));
    this.workTypeApi
      .list({ pageSize: 100 })
      .pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe((page) =>
        this.workTypes.set(page.items.filter((workType) => workType.status === 'ACTIVE')),
      );
    this.employeeSearch
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        switchMap((keyword) =>
          keyword
            ? this.employees
                .list({ keyword, status: 'ACTIVE', pageSize: MATCH_LIMIT })
                .pipe(catchError(() => EMPTY))
            : [null],
        ),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe((page) => this.matches.set(page?.items ?? []));
  }

  protected close(): void {
    if (!this.saving()) this.closed.emit();
  }

  protected onKeyword(keyword: string): void {
    this.employeeSearch.next(keyword.trim());
  }

  protected addParticipant(employee: EmployeeDetail): void {
    this.participants.update((current) => [...current, employee]);
    this.keyword = '';
    this.matches.set([]);
  }

  protected removeParticipant(id: string): void {
    this.participants.update((current) => current.filter((employee) => employee.id !== id));
  }

  protected isSelected(employeeId: string): boolean {
    return this.participants().some((employee) => employee.id === employeeId);
  }

  protected toggleWorkType(id: string): void {
    this.workTypeIds.update((current) =>
      current.includes(id) ? current.filter((value) => value !== id) : [...current, id],
    );
  }

  protected submit(): void {
    if (this.saving()) return;
    if (!this.isValid()) {
      this.error.set(this.localization.campaign.form.invalid);
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.campaigns
      .create({
        businessObjectiveId: this.objectiveId,
        code: this.code.trim(),
        name: this.name.trim(),
        description: this.description.trim() || null,
        startDate: this.startDate,
        endDate: this.endDate,
        workTypeIds: this.workTypeIds(),
        participantEmployeeIds: this.participants().map((employee) => employee.id),
      })
      .subscribe({
        next: (campaign) => this.created.emit(campaign.id),
        error: (error: unknown) => {
          this.saving.set(false);
          this.error.set(serverMessage(error) ?? this.localization.campaign.genericError);
        },
      });
  }

  private isValid(): boolean {
    return (
      !!this.code.trim() &&
      !!this.name.trim() &&
      !!this.objectiveId &&
      !!this.startDate &&
      !!this.endDate &&
      this.endDate >= this.startDate &&
      this.workTypeIds().length > 0 &&
      this.participants().length > 0
    );
  }
}
