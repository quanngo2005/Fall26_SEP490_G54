import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { EvaluationService } from '../../../core/evaluation.service';
import { PreCloseCheckItem, PreCloseCheckOverview } from '../../../core/evaluation.models';
import { I18nService } from '../../../core/i18n/i18n.service';

@Component({
  selector: 'app-pre-close-check-modal',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './pre-close-check-modal.component.html',
  styleUrl: './pre-close-check-modal.component.scss',
})
export class PreCloseCheckModalComponent {
  private readonly evaluationService = inject(EvaluationService);
  private readonly i18n = inject(I18nService);
  private readonly router = inject(Router);

  isOpen = input.required<boolean>();
  periodId = input.required<string>();

  closeModal = output<void>();
  continuePublish = output<void>();

  loading = signal<boolean>(false);
  error = signal<string | null>(null);
  overview = signal<PreCloseCheckOverview | null>(null);

  constructor() {
    effect(() => {
      if (this.isOpen() && this.periodId()) {
        this.loadChecklist();
      }
    });
  }

  protected readonly t = computed(() => {
    return this.i18n.localization.evaluations.preCloseCheck;
  });

  formattedDeadline = computed(() => {
    const deadline = this.overview()?.preCloseDeadline;
    if (!deadline) return '';
    try {
      const date = new Date(deadline);
      const day = String(date.getDate()).padStart(2, '0');
      const month = String(date.getMonth() + 1).padStart(2, '0');
      const year = date.getFullYear();
      const hours = String(date.getHours()).padStart(2, '0');
      const mins = String(date.getMinutes()).padStart(2, '0');
      return `${day}/${month}/${year} ${hours}:${mins}`;
    } catch {
      return '';
    }
  });

  bannerMessage = computed(() => {
    const ov = this.overview();
    if (!ov) return '';
    if (ov.totalUnresolvedCount > 0) {
      return (
        this.t()['bannerFailed'] || 'Còn {count} hạng mục chưa đạt. Chưa publish được.'
      ).replace('{count}', String(ov.totalUnresolvedCount));
    }
    return (
      this.t()['bannerPassed'] || 'Tất cả các hạng mục đều đạt. Có thể tiến hành publish kết quả.'
    );
  });

  loadChecklist(): void {
    const pid = this.periodId();
    if (!pid) return;

    this.loading.set(true);
    this.error.set(null);

    this.evaluationService.getPreCloseCheck(pid).subscribe({
      next: (data) => {
        this.overview.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(
          this.t()['errorLoad'] || 'Không thể kiểm tra các hạng mục. Vui lòng thử lại.',
        );
        this.loading.set(false);
      },
    });
  }

  onRecheck(): void {
    this.loadChecklist();
  }

  onClose(): void {
    this.closeModal.emit();
  }

  onContinue(): void {
    if (this.overview()?.canPublish) {
      this.continuePublish.emit();
    }
  }

  onNavigate(item: PreCloseCheckItem): void {
    if (item.targetRoute) {
      this.closeModal.emit();
      this.router.navigateByUrl(item.targetRoute);
    } else {
      this.closeModal.emit();
    }
  }
}
