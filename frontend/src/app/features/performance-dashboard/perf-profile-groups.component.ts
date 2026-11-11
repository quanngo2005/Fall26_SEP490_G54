import { Component, computed, input } from '@angular/core';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentPosition } from '../../core/department.models';
import { barWidth, formatValue, TRACK_HEADROOM } from './perf-display';

/**
 * "Điểm theo nhóm đánh giá": for each root group of the profiles, the average group score of every job position with
 * the position's weight. Groups are matched by the name the API returns; weak groups (API flag) are red.
 */
@Component({
  selector: 'app-perf-profile-groups',
  template: `
    @let text = localization.departmentPerformance;
<section class="card" aria-labelledby="profile-groups-title">
  <div class="card-head">
    <div>
      <h2 id="profile-groups-title">{{text.profileGroupsTitle}}</h2>
      <p class="muted hint">{{text.profileGroupsHint}}</p>
    </div>
  </div>
  @for (group of rows(); track group.name; let i = $index) {
    <div class="group">
      <h3>
        <span class="swatch" [attr.data-index]="i % 3" aria-hidden="true"></span>{{group.name}}
      </h3>
      @for (row of group.positions; track row.title) {
        <div class="row">
          <span class="title">{{row.title}}</span>
          <span class="track" aria-hidden="true">
            <span
              class="fill"
              [class.weak]="row.isWeak"
              [style.width.%]="width(row.average)"
            ></span>
            <span class="marker" [style.left.%]="width(100)"></span>
          </span>
          <b class="mono" [class.down]="row.isWeak">{{score(row.average)}}</b>
          <span class="chip mono" [attr.title]="text.weight">{{score(row.weight, 0)}}%</span>
        </div>
      }
    </div>
  } @empty {
    <p class="empty">{{text.noProfileGroups}}</p>
  }
</section>
  `,
  styleUrl: './perf-card.scss',
  styles: `
    .hint {
      margin: 2px 0 0;
      font-size: 12px;
    }
    .group + .group {
      margin-top: 14px;
      padding-top: 14px;
      border-top: 1px solid #f1f5f9;
    }
    h3 {
      margin: 0 0 10px;
      color: #0f172a;
      font-size: 14px;
    }
    .swatch {
      display: inline-block;
      width: 10px;
      height: 10px;
      margin-right: 8px;
      border-radius: 2px;
      background: #1e3a8a;
    }
    .swatch[data-index='1'] {
      background: #3b82f6;
    }
    .swatch[data-index='2'] {
      background: #bfdbfe;
    }
    .row {
      display: grid;
      grid-template-columns: minmax(60px, 90px) 1fr 44px 48px;
      align-items: center;
      gap: 10px;
      margin-bottom: 10px;
      font-size: 13px;
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
      background: #0f2a44;
    }
    .fill.weak {
      background: #e11d48;
    }
    .marker {
      position: absolute;
      top: -3px;
      width: 2px;
      height: 18px;
      background: #0f172a;
    }
    .chip {
      padding: 2px 6px;
      border-radius: 10px;
      background: #f1f5f9;
      font-size: 11px;
      text-align: center;
    }
  `,
})
export class PerfProfileGroupsComponent extends LocalizedComponent {
  readonly positions = input.required<DepartmentPosition[]>();

  /** Groups by name in display order, each with one row per position that has it. */
  protected readonly rows = computed(() => {
    const byName = new Map<
      string,
      {
        name: string;
        order: number;
        positions: { title: string; average: number | null; weight: number; isWeak: boolean }[];
      }
    >();
    for (const position of this.positions()) {
      for (const group of position.groups) {
        const entry = byName.get(group.name) ?? {
          name: group.name,
          order: group.displayOrder,
          positions: [],
        };
        entry.order = Math.min(entry.order, group.displayOrder);
        entry.positions.push({
          title: position.jobPositionTitle,
          average: group.average,
          weight: group.weightPercent,
          isWeak: group.isWeak,
        });
        byName.set(group.name, entry);
      }
    }
    return [...byName.values()].sort((a, b) => a.order - b.order);
  });

  protected width(value: number | null): number {
    return barWidth(value, 100 * TRACK_HEADROOM);
  }

  protected score(value: number | null, decimals = 1): string {
    return formatValue(this.i18n.currentLanguage(), value, decimals);
  }
}
