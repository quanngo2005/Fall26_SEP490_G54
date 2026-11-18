import { HttpErrorResponse } from '@angular/common/http';
import { A11yModule } from '@angular/cdk/a11y';
import { Component, HostListener, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';

export type KpiDecision = 'APPROVE' | 'REJECT';

const MAKER_CANNOT_BE_CHECKER = 'MAKER_CANNOT_BE_CHECKER';

/** SCR-STRAT-02-M3: the Branch Manager approves a KPI or rejects it with a reason. */
@Component({
  selector: 'app-kpi-decision-modal',
  standalone: true,
  imports: [A11yModule, FormsModule],
  templateUrl: './kpi-decision-modal.component.html',
  styleUrls: [
    '../../shared/ui/ui-controls.scss',
    '../../shared/ui/dialog.scss',
    './kpi-dialog.scss',
  ],
})
export class KpiDecisionModalComponent extends LocalizedComponent implements OnInit {
  private readonly api = inject(KpiDefinitionService);

  readonly kpi = input.required<KpiDefinition>();
  readonly initial = input<KpiDecision>('APPROVE');
  readonly closed = output<void>();
  /** The decision went through; the list reloads. */
  readonly decided = output<{ decision: KpiDecision; kpi: KpiDefinition }>();

  protected decision: KpiDecision = 'APPROVE';
  protected reason = '';

  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);
  protected readonly errorText = signal<string | null>(null);

  ngOnInit(): void {
    this.decision = this.initial();
  }

  /** The switch: on is Approve, off is Reject. */
  protected toggle(): void {
    this.decision = this.decision === 'APPROVE' ? 'REJECT' : 'APPROVE';
    this.errorText.set(null);
  }

  protected get reasonMissing(): boolean {
    return this.decision === 'REJECT' && this.reason.trim().length === 0;
  }

  @HostListener('document:keydown.escape')
  protected close(): void {
    if (!this.saving()) this.closed.emit();
  }

  protected submit(): void {
    this.submitted.set(true);
    if (this.reasonMissing || this.saving()) return;

    const kpi = this.kpi();
    const decision = this.decision;
    this.saving.set(true);
    this.errorText.set(null);
    const request =
      decision === 'APPROVE'
        ? this.api.approve(kpi.id)
        : this.api.reject(kpi.id, this.reason.trim());
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.decided.emit({ decision, kpi });
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.errorText.set(this.messageFor(error));
      },
    });
  }

  private messageFor(error: HttpErrorResponse): string {
    const text = this.localization.kpiCatalog;
    if (error.status === 422 && error.error?.errorCode === MAKER_CANNOT_BE_CHECKER)
      return text.toastMaker;
    if (error.status === 409 || error.status === 404) return text.toastConflict;
    return text.toastFailed;
  }
}
