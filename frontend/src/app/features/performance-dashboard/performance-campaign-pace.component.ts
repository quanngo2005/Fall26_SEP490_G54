import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { CampaignSummary } from '../../core/campaign.models';
import { formatArrow, formatValue, toneOf } from './performance-display';

/**
 * "Campaign đang chạy": progress of each running campaign against the time elapsed, and the pace gap (all three
 * computed by the API, BRANCH_DASHBOARD_CONTRACT.md §3).
 */
@Component({
  selector: 'app-perf-campaign-pace',
  imports: [RouterLink],
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
    <div class="row head">
      <span>{{text.colCampaign}}</span>
      <span>{{text.colProgress}} · │ {{text.colElapsed}}</span>
      <span></span>
      <span class="right">{{text.colPace}}</span>
    </div>
    @for (campaign of campaigns(); track campaign.id) {
      <a class="row item" [routerLink]="['/campaigns', campaign.id]">
        <span
          ><b>{{campaign.name}}</b> <small class="muted">{{subtitle(campaign)}}</small></span
        >
        <span class="track" aria-hidden="true">
          <span
            class="fill"
            [class.behind]="campaign.paceGap < 0"
            [style.width.%]="campaign.progressPercent"
          ></span>
          <span class="marker" [style.left.%]="campaign.timeElapsedPercent"></span>
        </span>
        <b class="mono" [class]="campaign.paceGap < 0 ? 'down' : 'up'">{{percent(campaign.progressPercent)}}</b>
        <span class="mono right" [class]="tone(campaign.paceGap)">{{arrow(campaign.paceGap)}}</span>
      </a>
    }
  } @else {
    <p class="empty">{{text.noCampaigns}}</p>
  }
</section>
  `,
  styleUrl: './performance-card.scss',
  styles: `
    .row {
      display: grid;
      grid-template-columns: minmax(140px, 1.2fr) 1fr 48px 56px;
      align-items: center;
      gap: 12px;
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
    .track {
      position: relative;
      height: 12px;
      border-radius: 3px;
      background: #eef2f7;
    }
    .fill {
      position: absolute;
      inset: 0 auto 0 0;
      border-radius: 3px;
      background: #059669;
    }
    .fill.behind {
      background: #e11d48;
    }
    .marker {
      position: absolute;
      top: -4px;
      width: 2px;
      height: 20px;
      background: #0f172a;
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

  protected percent(value: number): string {
    return `${formatValue(this.i18n.currentLanguage(), value, 0)}%`;
  }

  protected arrow(value: number): string {
    return formatArrow(this.i18n.currentLanguage(), value, 0);
  }

  protected tone(value: number): string {
    return toneOf(value) ?? '';
  }
}
