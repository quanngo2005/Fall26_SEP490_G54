import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { CampaignStatus, CampaignSummary } from '../../core/campaign.models';
import { AppDatePipe } from '../../shared/pipes';
import { StatusBadgeComponent, StatusBadgeTone } from '../../shared/ui';
import { formatValue } from './performance-display';

/**
 * "Campaign đang chạy": running campaigns with start date, status, progress bar, completed works, and deadline.
 */
@Component({
  selector: 'app-perf-campaign-pace',
  imports: [RouterLink, AppDatePipe, StatusBadgeComponent],
  template: `
    @let text = localization.branchPerformance;
    <section class="card is-clickable" aria-labelledby="campaign-title">
      <div class="card-head">
        <h2 id="campaign-title">{{text.campaignsTitle}}</h2>
        <a class="card-link" routerLink="/campaigns"
          >{{text.viewAll}}<span class="chevron" aria-hidden="true">›</span></a
        >
      </div>
      @if (failed()) {
        <div role="alert">
          <p class="down">{{text.campaignLoadError}}</p>
          <button type="button" class="card-link" (click)="retry.emit()">
            {{localization.departmentPerformance.retry}}
          </button>
        </div>
      } @else if (campaigns().length) {
        <div class="table-wrap">
          <div class="row head">
            <span>{{text.colCampaign}}</span>
            <span>{{text.colStartDate}}</span>
            <span>{{text.colStatus}}</span>
            <span>{{text.colProgress}}</span>
            <span class="right">{{text.colCompletedWorks}}</span>
            <span class="right">{{text.colDeadline}}</span>
          </div>
          @for (campaign of campaigns(); track campaign.id) {
            <a class="row item" routerLink="/campaigns/detail" [state]="{ campaignId: campaign.id }">
              <span>
                <b>{{campaign.name}}</b>
                <small class="muted">{{subtitle(campaign)}}</small>
              </span>
              <span class="mono nowrap">{{campaign.startDate | appDate}}</span>
              <span>
                <app-status-badge [tone]="badgeTone(campaign.status)">
                  {{statusLabel(campaign.status)}}
                </app-status-badge>
              </span>
              <span class="progress">
                <span class="track" aria-hidden="true">
                  <span class="fill" [style.width.%]="clampedProgress(campaign.progressPercent)"></span>
                </span>
                <b class="mono percent">{{percent(campaign.progressPercent)}}</b>
              </span>
              <span class="mono right">
                {{campaign.completedWorksCount}}/{{campaign.generatedWorksCount}}
              </span>
              <span class="mono right nowrap">{{campaign.endDate | appDate}}</span>
            </a>
          }
        </div>
      } @else {
        <p class="empty">{{text.noCampaigns}}</p>
      }
    </section>
  `,
  styleUrl: './performance-card.scss',
  styles: `
    .table-wrap {
      overflow-x: auto;
    }
    .row {
      display: grid;
      grid-template-columns: minmax(180px, 1.5fr) 110px 130px minmax(140px, 1.2fr) 170px 110px;
      align-items: center;
      gap: 12px;
      min-width: 780px;
      padding: 10px 0;
      border-bottom: 1px solid #f1f5f9;
      color: #0f172a;
      font-size: 14px;
      text-decoration: none;
    }
    .head {
      padding-top: 0;
      color: #475569;
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
    }
    .item small {
      display: block;
      font-size: 12px;
    }
    .right {
      text-align: right;
    }
    .nowrap {
      white-space: nowrap;
    }
    .progress {
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .track {
      flex: 1;
      height: 8px;
      overflow: hidden;
      border-radius: 9999px;
      background: #e2e8f0;
    }
    .fill {
      display: block;
      height: 100%;
      border-radius: 9999px;
      background: #0e7490;
      transition: width 0.2s ease;
    }
    .percent {
      min-width: 38px;
      text-align: right;
    }
  `,
})
export class PerfCampaignPaceComponent extends LocalizedComponent {
  readonly campaigns = input.required<CampaignSummary[]>();
  readonly failed = input(false);
  readonly retry = output<void>();

  /** "KPI · actual / target unit" when the API resolved the campaign's KPI, otherwise the work counts. */
  protected subtitle(campaign: CampaignSummary): string {
    const locale = this.i18n.currentLanguage();
    if (campaign.kpiName && campaign.targetValue != null) {
      const actual = formatValue(locale, campaign.actualValue);
      const target = formatValue(locale, campaign.targetValue);
      return `${campaign.kpiName} · ${actual} / ${target} ${campaign.targetUnit ?? ''}`.trim();
    }
    return `${campaign.completedWorksCount}/${campaign.generatedWorksCount} ${this.localization.departmentPerformance.worksUnit}`;
  }

  protected clampedProgress(value: number): number {
    return Math.max(0, Math.min(100, value ?? 0));
  }

  protected percent(value: number): string {
    return `${formatValue(this.i18n.currentLanguage(), value, 0)}%`;
  }

  protected statusLabel(status: CampaignStatus): string {
    return this.localization.campaign.status[status] ?? status;
  }

  protected badgeTone(status: CampaignStatus): StatusBadgeTone {
    switch (status) {
      case 'RUNNING':
      case 'COMPLETED':
        return 'success';
      case 'PLANNED':
      case 'PAUSED':
        return 'warning';
      case 'CANCELLED':
        return 'danger';
      default:
        return 'neutral';
    }
  }
}
