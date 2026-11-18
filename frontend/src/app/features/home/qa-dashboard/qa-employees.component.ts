import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { LocalizedComponent } from '../../../core/i18n/localized.component';
import { QaDashboardTopEmployee } from '../../../core/qa-dashboard.models';
import { TablePaginationComponent } from '../../../shared/ui';
import { fill } from './qa-dashboard-display';

export const EMPLOYEES_PAGE_SIZE = 5;

/** "Nhân viên có nhiều mã không khớp": compact table, 5 rows a page; a row opens that employee's unmatched codes. */
@Component({
  selector: 'app-qa-employees',
  imports: [RouterLink, TablePaginationComponent],
  template: `
    <section class="qa-panel" [attr.aria-label]="text().title">
  <header class="qa-panel-head">
    <h2>{{text().title}}</h2>
    <a class="qa-link" routerLink="/reconciliations" [queryParams]="listParams()">
      {{text().viewReconciliation}}
    </a>
  </header>
  @if (employees().length > 0) {
    <div class="table-box">
      <table>
        <thead>
          <tr>
            <th scope="col">{{text().colEmployee}}</th>
            <th scope="col" class="position">{{text().colPosition}}</th>
            <th scope="col" class="num">{{text().colNotMatched}}</th>
            <th scope="col" class="num flags">{{text().colOpenFlags}}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of pageRows(); track row.employeeId) {
            <tr (click)="open(row)">
              <td class="name">
                <a
                  routerLink="/reconciliations"
                  [queryParams]="rowParams(row)"
                  [attr.aria-label]="rowAria(row)"
                  (click)="$event.stopPropagation()"
                  [title]="row.employeeCode + ' · ' + row.fullName"
                  >{{row.employeeCode}} · {{row.fullName}}</a
                >
              </td>
              <td class="position" [title]="row.jobTitle">{{row.jobTitle}}</td>
              <td class="num danger">{{row.discrepancyCount}}</td>
              <td class="num">{{row.flagCount}}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
    <app-table-pagination
      class="pager"
      [page]="page()"
      [pageSize]="pageSize"
      [totalCount]="employees().length"
      [rangeTemplate]="text().range"
      [ariaLabel]="text().pagination"
      (pageChange)="page.set($event)"
    />
  } @else {
    <p class="qa-empty">{{text().empty}}</p>
  }
</section>
  `,
  styleUrl: './qa-panel.scss',
  styles: `
    .table-box {
      margin: 0 16px;
      overflow: hidden;
      border: 1px solid #eef2f6;
      border-radius: 8px;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      table-layout: fixed;
      font-size: 13px;
    }
    th {
      background: #f1f5f9;
      color: #475569;
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.02em;
      text-align: left;
      line-height: 14px;
      text-transform: uppercase;
    }
    th,
    td {
      overflow: hidden;
      padding: 0 12px;
      text-overflow: ellipsis;
    }
    td {
      white-space: nowrap;
    }
    th {
      height: 36px;
    }
    td {
      height: 44px;
      border-top: 1px solid #f1f5f9;
      color: #334155;
    }
    tbody tr {
      cursor: pointer;
      &:hover {
        background: #f8fafc;
      }
    }
    .name {
      a {
        color: #0f172a;
        text-decoration: none;
        &:focus-visible {
          outline: 2px solid #2563eb;
        }
      }
    }
    th.position {
      width: 28%;
    }
    .num {
      width: 104px;
      text-align: right;
      font-variant-numeric: tabular-nums;
    }
    th.num {
      width: 104px;
      &.flags {
        width: 88px;
      }
    }
    .danger {
      color: #dc2626;
      font-weight: 600;
    }
    .pager {
      margin-top: auto;
    }
    :host {
      container-type: inline-size;
    }
    @container (max-width: 520px) {
      .position {
        display: none;
      }
      th,
      td {
        padding: 0 8px;
      }
      th.num.flags {
        width: 60px;
      }
    }
  `,
})
export class QaEmployeesComponent extends LocalizedComponent {
  private readonly router = inject(Router);

  readonly employees = input.required<QaDashboardTopEmployee[]>();
  /** Dashboard filters, carried over to the reconciliation screen. */
  readonly linkParams = input<Record<string, string>>({});

  protected readonly pageSize = EMPLOYEES_PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly text = computed(() => this.localization.qaDashboard.employees);

  protected readonly pageRows = computed(() => {
    const start = (this.page() - 1) * EMPLOYEES_PAGE_SIZE;
    return this.employees().slice(start, start + EMPLOYEES_PAGE_SIZE);
  });

  protected readonly listParams = computed(() => ({
    ...this.linkParams(),
    status: 'NOT_MATCHED',
  }));

  constructor() {
    super();
    // New data (other filters) starts again on the first page.
    effect(() => {
      this.employees();
      this.page.set(1);
    });
  }

  protected rowParams(row: QaDashboardTopEmployee): Record<string, string> {
    // The reconciliation list reads its search text from `q`.
    return { ...this.listParams(), q: row.employeeCode };
  }

  protected rowAria(row: QaDashboardTopEmployee): string {
    return fill(this.text().rowAria, { name: `${row.employeeCode} · ${row.fullName}` });
  }

  protected open(row: QaDashboardTopEmployee): void {
    void this.router.navigate(['/reconciliations'], { queryParams: this.rowParams(row) });
  }
}
