import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentAttentionMember } from '../../core/department.models';
import { formatArrow, formatValue, toneOf } from './performance-display';

/**
 * "Cán bộ cần chú ý": the API's attention list (rules and size are system parameters on the API), each row opening the
 * employee's detail.
 */
@Component({
  selector: 'app-perf-attention',
  imports: [RouterLink],
  template: `
    @let text = localization.departmentPerformance;
<section class="card is-clickable" aria-labelledby="attention-title">
  <div class="card-head">
    <h2 id="attention-title">{{text.attentionTitle}}</h2>
    <button type="button" class="card-link" (click)="viewAll.emit()">
      {{text.viewAll}} {{employeeCount()}}<span class="chevron" aria-hidden="true">›</span>
    </button>
  </div>
  @if (members().length) {
    <table>
      <thead>
        <tr>
          <th scope="col">{{text.colEmployee}}</th>
          <th scope="col" class="num">{{text.colTotal}} {{periodName()}}</th>
          @if (compareName()) {
            <th scope="col" class="num">± {{compareName()}}</th>
          }
          <th scope="col">{{text.colWeakest}}</th>
          <th scope="col" class="num">{{text.overdue}}</th>
          <th scope="col">
            <span class="sr-only">{{text.view}}</span>
          </th>
        </tr>
      </thead>
      <tbody>
        @for (member of members(); track member.employeeId) {
          <tr>
            <td>
              <b>{{member.fullName}}</b
              ><small class="muted">{{member.jobPositionTitle}}</small>
            </td>
            <td class="num mono strong">{{score(member.totalScore)}}</td>
            @if (compareName()) {
              <td class="num mono" [class]="tone(member.changeFromPrevious)">
                {{arrow(member.changeFromPrevious)}}
              </td>
            }
            <td class="down">
              @if (member.weakestGroup; as weakest) {
                {{weakest.name}} {{score(weakest.score)}}
              } @else {
                —
              }
            </td>
            <td class="num mono" [class.warn]="member.overdueWorkCount > 0">
              {{member.overdueWorkCount || '—'}}
            </td>
            <td class="num">
              <a
                class="row-link"
                [routerLink]="['/employee-detail', member.employeeCode]"
                [attr.aria-label]="text.view + ' ' + member.fullName"
                >›</a
              >
            </td>
          </tr>
        }
      </tbody>
    </table>
  } @else {
    <p class="empty">{{text.noAttention}}</p>
  }
</section>
  `,
  styleUrl: './performance-card.scss',
  styles: `
    table {
      position: relative;
      width: 100%;
      border-collapse: collapse;
      font-size: 14px;
    }
    th,
    td {
      padding: 8px 6px;
      border-bottom: 1px solid #f1f5f9;
      text-align: left;
    }
    th {
      color: #475569;
      font-size: 12px;
      font-weight: 600;
      text-transform: uppercase;
    }
    td small {
      display: block;
      font-size: 12px;
    }
    .num {
      text-align: right;
      white-space: nowrap;
    }
    .strong {
      font-weight: 700;
    }
    .row-link {
      color: #94a3b8;
      font-size: 18px;
      text-decoration: none;
    }
    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip: rect(0 0 0 0);
    }
  `,
})
export class PerfAttentionComponent extends LocalizedComponent {
  readonly members = input.required<DepartmentAttentionMember[]>();
  readonly employeeCount = input.required<number>();
  readonly periodName = input<string | null>(null);
  readonly compareName = input<string | null>(null);
  readonly viewAll = output<void>();

  protected score(value: number | null): string {
    return formatValue(this.i18n.currentLanguage(), value);
  }

  protected arrow(value: number | null): string {
    return formatArrow(this.i18n.currentLanguage(), value);
  }

  protected tone(value: number | null): string {
    return toneOf(value) ?? '';
  }
}
