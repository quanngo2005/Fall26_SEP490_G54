import { HttpErrorResponse } from '@angular/common/http';
import { Component, HostListener, computed, inject, input, output, signal } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import { FormsModule } from '@angular/forms';
import { DepartmentMember } from '../../core/department.models';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { WorkTypeDetail } from '../../core/work-type.models';
import { WorkDetail, WorkPriority } from '../../core/work.models';
import { WorkService } from '../../core/work.service';
import { businessClock } from './work-board.utils';

const MAX_CHECKLIST_ITEMS = 20;

/** SCR-WRK-02: the department manager assigns a work to an employee (`POST /works`). */
@Component({
  selector: 'app-assign-work-dialog',
  standalone: true,
  imports: [A11yModule, FormsModule],
  templateUrl: './assign-work-dialog.component.html',
  styleUrls: ['../../shared/ui/ui-controls.scss', '../../shared/ui/dialog.scss'],
})
export class AssignWorkDialogComponent extends LocalizedComponent {
  private readonly works = inject(WorkService);

  readonly members = input.required<readonly DepartmentMember[]>();
  readonly workTypes = input.required<readonly WorkTypeDetail[]>();
  readonly closed = output<void>();
  readonly assigned = output<WorkDetail>();

  protected readonly priorities: readonly WorkPriority[] = ['Critical', 'High', 'Medium', 'Low'];
  protected readonly today = businessClock(new Date()).date;
  protected readonly maxItems = MAX_CHECKLIST_ITEMS;

  protected assigneeId = '';
  protected workTypeId = '';
  protected dueDate = '';
  protected priority: WorkPriority = 'Medium';
  protected readonly checklist = signal<string[]>([]);

  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);
  protected readonly errorText = signal<string | null>(null);

  protected readonly activeMembers = computed(() =>
    this.members().filter((member) => member.status === 'ACTIVE'),
  );
  protected readonly activeWorkTypes = computed(() =>
    this.workTypes().filter((type) => type.status === 'ACTIVE'),
  );

  @HostListener('document:keydown.escape')
  protected close(): void {
    if (!this.saving()) this.closed.emit();
  }

  protected addItem(): void {
    if (this.checklist().length < MAX_CHECKLIST_ITEMS) this.checklist.update((l) => [...l, '']);
  }

  protected editItem(index: number, value: string): void {
    this.checklist.update((list) => list.map((item, i) => (i === index ? value : item)));
  }

  protected removeItem(index: number): void {
    this.checklist.update((list) => list.filter((_, i) => i !== index));
  }

  protected get dueInPast(): boolean {
    return this.dueDate !== '' && this.dueDate < this.today;
  }

  protected submit(): void {
    this.submitted.set(true);
    const workType = this.activeWorkTypes().find((type) => type.id === this.workTypeId);
    if (!this.assigneeId || !workType || !this.dueDate || this.dueInPast) return;

    this.saving.set(true);
    this.errorText.set(null);
    const items = this.checklist()
      .map((item) => item.trim())
      .filter((item) => item !== '');
    this.works
      .create({
        title: workType.name,
        workTypeId: workType.id,
        assigneeEmployeeId: this.assigneeId,
        priority: this.priority,
        plannedEndDate: this.dueDate,
        ...(items.length > 0 ? { checklistItems: items } : {}),
      })
      .subscribe({
        next: (work) => {
          this.saving.set(false);
          this.assigned.emit(work);
        },
        error: (error: HttpErrorResponse) => {
          this.saving.set(false);
          const text = this.localization.work.assign;
          this.errorText.set(
            error.status === 403
              ? text.forbidden
              : error.status === 400
                ? text.invalid
                : text.error,
          );
        },
      });
  }
}
