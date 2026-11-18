import { HttpErrorResponse } from '@angular/common/http';
import { A11yModule } from '@angular/cdk/a11y';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import {
  Component,
  HostListener,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { WorkService } from '../../core/work.service';
import { WorkDetail, WorkPlanChecklistItem, WorkPriority } from '../../core/work.models';
import { dateOnly } from '../work-board/work-board.utils';

/** Same limit as the server (`MaxChecklistItems`). */
export const MAX_CHECKLIST_ITEMS = 30;
const MAX_TITLE_LENGTH = 200;
const MAX_NOTE_LENGTH = 1000;

const PRIORITIES: readonly WorkPriority[] = ['Low', 'Medium', 'High', 'Critical'];

interface DraftItem {
  /** Only for the track-by of the list; never sent. */
  key: string;
  /** Absent for an item that was just added. */
  id?: string;
  title: string;
}

/**
 * SCR-WRK-03-M1: the manager changes the deadline, priority and checklist of a work and may leave a note for the
 * assignee (`PUT /works/{id}/plan`). Items that are already done are shown first and cannot be changed.
 */
@Component({
  selector: 'app-work-plan-dialog',
  standalone: true,
  imports: [A11yModule, DragDropModule, FormsModule],
  templateUrl: './work-plan-dialog.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/dialog.scss',
    './work-plan-dialog.component.scss',
  ],
})
export class WorkPlanDialogComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(WorkService);
  private nextKey = 0;

  readonly work = input.required<WorkDetail>();
  readonly closed = output<void>();
  readonly saved = output<WorkDetail>();

  protected readonly priorities = PRIORITIES;
  protected readonly maxTitle = MAX_TITLE_LENGTH;
  protected readonly maxNote = MAX_NOTE_LENGTH;
  protected readonly maxItems = MAX_CHECKLIST_ITEMS;

  protected readonly doneItems = signal<DraftItem[]>([]);
  protected readonly openItems = signal<DraftItem[]>([]);
  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);
  protected readonly errorText = signal<string | null>(null);

  protected dueDate = '';
  protected priority: WorkPriority = 'Medium';
  protected note = '';

  protected readonly total = computed(() => this.doneItems().length + this.openItems().length);

  ngOnInit(): void {
    const work = this.work();
    this.dueDate = work.plannedEndDate ?? '';
    this.priority = work.priority;
    const items = [...(work.checklistItems ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);
    this.doneItems.set(items.filter((i) => i.isDone).map((i) => this.draft(i.title, i.id)));
    this.openItems.set(items.filter((i) => !i.isDone).map((i) => this.draft(i.title, i.id)));
  }

  @HostListener('document:keydown.escape')
  protected close(): void {
    if (!this.saving()) this.closed.emit();
  }

  protected previousDeadline(): string | null {
    const end = this.work().plannedEndDate;
    return end ? this.i18n.formatDate(dateOnly(end)) : null;
  }

  protected addItem(): void {
    if (this.total() >= MAX_CHECKLIST_ITEMS) return;
    this.openItems.update((list) => [...list, this.draft('')]);
  }

  protected removeItem(index: number): void {
    this.openItems.update((list) => list.filter((_, i) => i !== index));
  }

  protected rename(index: number, title: string): void {
    this.openItems.update((list) =>
      list.map((item, i) => (i === index ? { ...item, title } : item)),
    );
  }

  protected reorder(event: CdkDragDrop<DraftItem[]>): void {
    const list = [...this.openItems()];
    moveItemInArray(list, event.previousIndex, event.currentIndex);
    this.openItems.set(list);
  }

  protected get dueMissing(): boolean {
    return !this.dueDate;
  }

  protected get hasBlankTitle(): boolean {
    return this.openItems().some((item) => !item.title.trim());
  }

  protected submit(): void {
    this.submitted.set(true);
    if (this.dueMissing || this.hasBlankTitle || this.total() > MAX_CHECKLIST_ITEMS) return;

    const checklist: WorkPlanChecklistItem[] = [...this.doneItems(), ...this.openItems()].map(
      (item) => ({ id: item.id, title: item.title.trim() }),
    );
    const note = this.note.trim();

    this.saving.set(true);
    this.errorText.set(null);
    this.api
      .updatePlan(this.work().id, {
        plannedEndDate: this.dueDate,
        priority: this.priority,
        checklist,
        ...(note ? { note } : {}),
      })
      .subscribe({
        next: (updated) => {
          this.saving.set(false);
          this.saved.emit(updated);
        },
        error: (error: HttpErrorResponse) => {
          this.saving.set(false);
          this.errorText.set(this.messageFor(error));
        },
      });
  }

  private draft(title: string, id?: string): DraftItem {
    return { key: `item-${this.nextKey++}`, id, title };
  }

  private messageFor(error: HttpErrorResponse): string {
    const text = this.localization.work.detail.plan;
    if (error.status === 403) return text.errorForbidden;
    if (error.status === 409) return text.errorClosed;
    if (error.status === 400) {
      const message: unknown = error.error?.message;
      return typeof message === 'string' && message.trim() ? message : text.errorInvalid;
    }
    return text.errorGeneric;
  }
}
