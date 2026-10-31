import { Component, input, output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { DepartmentSummary } from '../../core/department.models';
import { DepartmentService } from '../../core/department.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { DepartmentDetailDrawerComponent } from './department-detail-drawer.component';
import { DepartmentListComponent } from './department-list.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}
@Component({ selector: 'app-department-detail-drawer', template: '' })
class DrawerStub {
  readonly department = input.required<DepartmentSummary>();
  readonly closeDrawer = output<void>();
}

function dept(
  code: string,
  parent: string | null,
  extra: Partial<DepartmentSummary> = {},
): DepartmentSummary {
  return {
    id: `id-${code}`,
    code,
    name: `Name ${code}`,
    levelType: parent ? 'DEPARTMENT' : 'BRANCH',
    status: 'ACTIVE',
    parentDepartmentId: parent ? `id-${parent}` : null,
    parentDepartmentName: parent ? `Name ${parent}` : null,
    employeeCount: 13,
    childDepartmentCount: 0,
    managerName: `Manager ${code}`,
    tellerCount: 8,
    salesCount: 5,
    lastSyncAt: '2026-10-24T08:15:00+07:00',
    ...extra,
  };
}

describe('DepartmentListComponent', () => {
  const api = { list: jasmine.createSpy('list') };
  const items = [
    dept('B', null),
    dept('B-D1', 'B'),
    dept('B-D2', 'B', { managerName: null, tellerCount: 0, salesCount: 0, employeeCount: 0 }),
  ];
  let query: Record<string, string>;
  let i18n: I18nService;

  function page(list: DepartmentSummary[], totalCount = list.length, pageNumber = 1) {
    return of({ items: list, totalCount, page: pageNumber, pageSize: 20 });
  }

  async function create(): Promise<ComponentFixture<DepartmentListComponent>> {
    const fixture = TestBed.createComponent(DepartmentListComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const rows = (fixture: ComponentFixture<unknown>) =>
    Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
        'tbody tr.department-row',
      ),
    );
  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  beforeEach(async () => {
    query = {};
    api.list.calls.reset();
    api.list.and.callFake(() => page(items));
    await TestBed.configureTestingModule({
      imports: [DepartmentListComponent],
      providers: [
        provideRouter([]),
        { provide: DepartmentService, useValue: api },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              get queryParamMap() {
                return convertToParamMap(query);
              },
            },
          },
        },
      ],
    })
      .overrideComponent(DepartmentListComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent, DepartmentDetailDrawerComponent] },
        add: { imports: [HeaderStub, SidebarStub, DrawerStub] },
      })
      .compileComponents();
    i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');
  });

  it('requests the first page from the API and renders 6 columns matching mockup', async () => {
    const fixture = await create();

    expect(api.list).toHaveBeenCalledWith({
      keyword: undefined,
      status: undefined,
      page: 1,
      pageSize: 20,
    });
    const rendered = rows(fixture);
    expect(rendered.map((row) => text(row.querySelector('.code-badge')))).toEqual([
      'B',
      'B-D1',
      'B-D2',
    ]);
    expect(text(rendered[0].querySelector('.col-manager'))).toBe('Manager B');
    expect(rendered[0].querySelectorAll('.col-num')[0].textContent?.trim()).toBe('8');
    expect(rendered[0].querySelectorAll('.col-num')[1].textContent?.trim()).toBe('5');
    expect(rendered[0].querySelectorAll('.col-num')[2].textContent?.trim()).toBe('13');
  });

  it('shows N/A for an unknown manager and 0 for a genuine zero count, never an invented value', async () => {
    const fixture = await create();
    const row = rows(fixture)[2];

    expect(text(row.querySelector('.col-manager'))).toBe('N/A');
    const counts = Array.from(row.querySelectorAll('.col-num')).map((cell) =>
      cell.textContent?.trim(),
    );
    expect(counts).toEqual(['0', '0', '0']);
  });

  it('derives the sync banner from the data and has no invented timestamp', async () => {
    const fixture = await create();
    const banner = text((fixture.nativeElement as HTMLElement).querySelector('.sync-banner'));
    expect(banner).toMatch(/đồng bộ 2\d\/10\/2026 \d{2}:\d{2}$/);

    api.list.and.callFake(() => page([]));
    const empty = await create();
    expect(text((empty.nativeElement as HTMLElement).querySelector('.sync-banner'))).toBe(
      'Chưa có thông tin đồng bộ',
    );
    expect(text((empty.nativeElement as HTMLElement).querySelector('.sync-banner'))).not.toContain(
      '2026',
    );
  });

  it('shows the empty state for a real empty response, and a different message when filters are active', async () => {
    api.list.and.callFake(() => page([]));
    const fixture = await create();
    expect(text((fixture.nativeElement as HTMLElement).querySelector('.empty-cell'))).toBe(
      'Chưa có phòng ban trong phạm vi truy cập.',
    );

    const component = fixture.componentInstance as unknown as { keyword: string; refresh(): void };
    component.keyword = 'zzz';
    component.refresh();
    fixture.detectChanges();
    expect(text((fixture.nativeElement as HTMLElement).querySelector('.empty-cell'))).toBe(
      'Không tìm thấy phòng ban phù hợp với bộ lọc.',
    );
  });

  it('shows the error state with a working retry and does not substitute sample data', async () => {
    api.list.and.returnValues(
      throwError(() => new Error('500')),
      page(items),
    );
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[role="alert"]')).not.toBeNull();
    expect(rows(fixture).length).toBe(0);

    root.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    fixture.detectChanges();

    expect(api.list).toHaveBeenCalledTimes(2);
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(rows(fixture).length).toBe(3);
  });

  it('sends the trimmed keyword and status filter and returns to page 1', async () => {
    const fixture = await create();
    const component = fixture.componentInstance as unknown as {
      keyword: string;
      status: string;
      refresh(): void;
    };

    component.keyword = '  sales ';
    component.status = 'INACTIVE';
    component.refresh();

    expect(api.list.calls.mostRecent().args[0]).toEqual({
      keyword: 'sales',
      status: 'INACTIVE',
      page: 1,
      pageSize: 20,
    });
  });

  it('pages through the result and renders page number buttons', async () => {
    api.list.and.callFake((filter: { page: number }) => page(items, 45, filter.page));
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;
    const pageButtons = Array.from(
      root.querySelectorAll<HTMLButtonElement>('.pagination-controls button'),
    );

    expect(pageButtons.length).toBe(3);
    expect(text(root.querySelector('.pagination-info'))).toBe('1–20 / 45 phòng');

    pageButtons[1].click();
    fixture.detectChanges();
    expect(api.list.calls.mostRecent().args[0].page).toBe(2);
  });

  it('selects a row on click and keeps the drawer closed until then', async () => {
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('app-department-detail-drawer')).toBeNull();
    rows(fixture)[1].click();
    fixture.detectChanges();

    expect(rows(fixture)[1].classList).toContain('is-selected');
    expect(rows(fixture)[1].getAttribute('aria-selected')).toBe('true');
    expect(root.querySelector('app-department-detail-drawer')).not.toBeNull();
  });

  it('moves the selection with the arrow keys, follows the visible order and closes with Escape', async () => {
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;
    const press = (key: string, target: EventTarget = document.body) =>
      target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    const selected = () => text(root.querySelector('tr.is-selected .code-badge'));

    press('ArrowDown');
    fixture.detectChanges();
    expect(selected()).toBe('B');
    press('ArrowDown');
    fixture.detectChanges();
    expect(selected()).toBe('B-D1');
    press('ArrowUp');
    press('ArrowUp');
    fixture.detectChanges();
    expect(selected()).toBe('B-D2'); // wraps to the last row

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(root.querySelector('app-department-detail-drawer')).toBeNull();
  });

  it('does not hijack the arrow keys while the user types in the search box or uses the status select', async () => {
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;

    for (const control of [root.querySelector('input'), root.querySelector('select')]) {
      const event = new KeyboardEvent('keydown', {
        key: 'ArrowDown',
        bubbles: true,
        cancelable: true,
      });
      control?.dispatchEvent(event);
      expect(event.defaultPrevented).toBeFalse();
    }
    fixture.detectChanges();
    expect(root.querySelector('tr.is-selected')).toBeNull();
  });

  it('preselects the department named by ?department=<code> or the legacy ?departmentId=', async () => {
    query = { department: 'B-D1' };
    const byCode = await create();
    expect(
      text((byCode.nativeElement as HTMLElement).querySelector('tr.is-selected .code-badge')),
    ).toBe('B-D1');

    query = { departmentId: 'id-B-D2' };
    const byId = await create();
    expect(
      text((byId.nativeElement as HTMLElement).querySelector('tr.is-selected .code-badge')),
    ).toBe('B-D2');

    query = { department: 'UNKNOWN' };
    const none = await create();
    expect((none.nativeElement as HTMLElement).querySelector('tr.is-selected')).toBeNull();
  });

  it('opens nothing by default instead of silently selecting the first row', async () => {
    const fixture = await create();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('app-department-detail-drawer'),
    ).toBeNull();
  });

  it('drops a selection that is no longer on the loaded page', async () => {
    const fixture = await create();
    rows(fixture)[0].click();
    fixture.detectChanges();

    api.list.and.callFake(() => page([dept('X', null)]));
    (fixture.componentInstance as unknown as { refresh(): void }).refresh();
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('app-department-detail-drawer'),
    ).toBeNull();
  });

  it('ignores a stale response when a newer request is already in flight', async () => {
    const slow = new Subject<{
      items: DepartmentSummary[];
      totalCount: number;
      page: number;
      pageSize: number;
    }>();
    api.list.and.returnValues(slow, page([dept('NEW', null)]));
    const fixture = await create();
    (fixture.componentInstance as unknown as { refresh(): void }).refresh();
    fixture.detectChanges();
    slow.next({ items: [dept('OLD', null)], totalCount: 1, page: 1, pageSize: 20 });
    fixture.detectChanges();

    expect(rows(fixture).map((row) => text(row.querySelector('.code-badge')))).toEqual(['NEW']);
  });

  it('switches every label, accessibility text and the sync banner language live', async () => {
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;

    expect(text(root.querySelector('h1'))).toBe('Phòng ban');
    expect(root.querySelector('input')?.getAttribute('aria-label')).toBe('Tìm mã hoặc tên phòng');
    expect(text(root.querySelector('.sync-banner'))).toContain('đồng bộ');

    i18n.setLanguage('en');
    fixture.detectChanges();

    expect(text(root.querySelector('h1'))).toBe('Departments');
    expect(root.querySelector('input')?.getAttribute('aria-label')).toBe(
      'Search code or department name',
    );
    expect(text(root.querySelector('.sync-banner'))).toContain('synced');
    expect(text(root.querySelector('th.col-manager'))).toBe('MANAGER');
    expect(text(root.querySelector('.pagination-info'))).toBe('1–3 / 3 departments');
  });
});
