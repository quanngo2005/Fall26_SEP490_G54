import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DataTableCellDirective, DataTableComponent, TableColumn } from './data-table.component';

interface Row {
  name: string;
  score: number;
}

@Component({
  imports: [DataTableComponent, DataTableCellDirective],
  template: `
    <app-data-table
      caption="Test table"
      [columns]="columns"
      [rows]="rows()"
      [pageSize]="2"
      [loading]="loading()"
      [errorMessage]="error()"
      [rowClickable]="true"
      (rowClick)="clicked = $event"
    >
      <ng-template appCell="name" let-row
        ><b class="custom">{{ row.name }}</b></ng-template
      >
    </app-data-table>
  `,
})
class HostComponent {
  readonly columns: readonly TableColumn[] = [
    { key: 'name', header: 'Name', sortable: true },
    { key: 'score', header: 'Score', sortable: true, align: 'end' },
  ];
  readonly rows = signal<Row[]>([
    { name: 'Charlie', score: 3 },
    { name: 'Alice', score: 10 },
    { name: 'Bob', score: 2 },
  ]);
  readonly loading = signal(false);
  readonly error = signal('');
  clicked: Row | null = null;
}

describe('DataTableComponent', () => {
  function setup() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const names = () => Array.from(el.querySelectorAll('.custom')).map((n) => n.textContent);
    return { fixture, el, names, host: fixture.componentInstance };
  }

  it('paginates rows and renders custom cell templates', () => {
    const { fixture, el, names } = setup();
    expect(names()).toEqual(['Charlie', 'Alice']);
    expect(el.querySelector('.pager__range')?.textContent).toContain('1–2 / 3');

    (el.querySelectorAll('.pager .btn')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(names()).toEqual(['Bob']);
  });

  it('sorts ascending then descending and resets to the first page', () => {
    const { fixture, el, names } = setup();
    const nameSort = el.querySelector('th .sort') as HTMLButtonElement;

    nameSort.click();
    fixture.detectChanges();
    expect(names()).toEqual(['Alice', 'Bob']);
    expect(el.querySelector('th')?.getAttribute('aria-sort')).toBe('ascending');

    nameSort.click();
    fixture.detectChanges();
    expect(names()).toEqual(['Charlie', 'Bob']);
  });

  it('emits rowClick when a row is activated by keyboard', () => {
    const { fixture, el, host } = setup();
    const row = el.querySelector('tbody tr') as HTMLElement;
    row.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();
    expect(host.clicked?.name).toBe('Charlie');
  });

  it('shows skeleton, empty and error states', () => {
    const { fixture, el, host } = setup();

    host.loading.set(true);
    fixture.detectChanges();
    expect(el.querySelectorAll('app-skeleton').length).toBeGreaterThan(0);

    host.loading.set(false);
    host.rows.set([]);
    fixture.detectChanges();
    expect(el.querySelector('app-empty-state')?.textContent).toContain('Không có dữ liệu');

    host.error.set('Máy chủ không phản hồi');
    fixture.detectChanges();
    expect(el.querySelector('app-empty-state')?.textContent).toContain('Máy chủ không phản hồi');
  });
});
