import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, HostListener, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { AppNumberPipe } from '../../shared/pipes';
import {
  EVALUATION_PERIODS_MOCK,
  EvaluationPeriodOption,
  KpiCriteriaItem,
  MOCK_LEDGER_DATA,
  PERSONAL_EVALUATION_MOCK_Q1,
  PERSONAL_EVALUATION_MOCK_Q2,
  PersonalEvaluationView,
  TransactionLedgerItem,
} from './my-evaluation.models';

@Component({
  selector: 'app-my-evaluation',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    HeaderComponent,
    SidebarComponent,
    AppNumberPipe,
  ],
  templateUrl: './my-evaluation.component.html',
  styleUrls: [
    './my-evaluation.component.scss',
    './my-evaluation.cards.scss',
    './my-evaluation.table.scss',
    './my-evaluation.dialogs.scss',
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyEvaluationComponent extends LocalizedComponent {
  private readonly router = inject(Router);

  protected readonly periods = signal<readonly EvaluationPeriodOption[]>(EVALUATION_PERIODS_MOCK);
  protected readonly selectedPeriodId = signal<string>('2026-Q2');
  protected readonly evaluation = signal<PersonalEvaluationView>(PERSONAL_EVALUATION_MOCK_Q2);

  protected readonly showAcknowledgeModal = signal<boolean>(false);
  protected readonly acknowledgeSuccess = signal<boolean>(false);
  protected readonly acknowledging = signal<boolean>(false);

  protected readonly selectedDrilldownCriteria = signal<KpiCriteriaItem | null>(null);
  protected readonly ledgerItems = signal<readonly TransactionLedgerItem[]>([]);

  @HostListener('keydown.escape')
  onEscape(): void {
    if (this.showAcknowledgeModal()) {
      this.closeAcknowledgeModal();
    } else if (this.selectedDrilldownCriteria()) {
      this.closeDrilldown();
    }
  }

  onPeriodChange(periodId: string): void {
    this.selectedPeriodId.set(periodId);
    this.acknowledgeSuccess.set(false);
    this.closeDrilldown();

    if (periodId === '2026-Q2') {
      this.evaluation.set(PERSONAL_EVALUATION_MOCK_Q2);
    } else if (periodId === '2026-Q1') {
      this.evaluation.set(PERSONAL_EVALUATION_MOCK_Q1);
    } else {
      this.evaluation.set({
        ...PERSONAL_EVALUATION_MOCK_Q1,
        periodId,
        periodName: periodId,
        isAcknowledged: true,
      });
    }
  }

  openAcknowledgeModal(): void {
    if (this.evaluation().isAcknowledged) {
      return;
    }
    this.showAcknowledgeModal.set(true);
  }

  closeAcknowledgeModal(): void {
    this.showAcknowledgeModal.set(false);
  }

  confirmAcknowledge(): void {
    this.acknowledging.set(true);
    // Simulate instantaneous acknowledgment confirmation
    const current = this.evaluation();
    this.evaluation.set({
      ...current,
      isAcknowledged: true,
      acknowledgedAt: new Date().toISOString(),
    });
    this.acknowledging.set(false);
    this.showAcknowledgeModal.set(false);
    this.acknowledgeSuccess.set(true);
  }

  openAppeal(): void {
    void this.router.navigate(['/appeals/mine']);
  }

  openDrilldown(crit: KpiCriteriaItem): void {
    if (!crit.hasDrilldown) {
      return;
    }
    this.selectedDrilldownCriteria.set(crit);
    const items = MOCK_LEDGER_DATA[crit.id] ?? [];
    this.ledgerItems.set(items);
  }

  closeDrilldown(): void {
    this.selectedDrilldownCriteria.set(null);
    this.ledgerItems.set([]);
  }

  formatRatio(val: number): string {
    const formatted = val.toString().replace('.', ',');
    return `× ${formatted}`;
  }
}
