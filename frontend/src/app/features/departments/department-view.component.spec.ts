import { Location } from '@angular/common';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { DepartmentService } from '../../core/department.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { memberFixture, overviewFixture } from '../performance-dashboard/performance-fixtures.spec';
import { DepartmentViewComponent } from './department-view.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

describe('DepartmentViewComponent', () => {
  const api = {
    list: jasmine.createSpy('list'),
    overview: jasmine.createSpy('overview'),
    members: jasmine.createSpy('members'),
    exportOverview: jasmine.createSpy('exportOverview'),
  };
  const items = [
    { id: 'id-branch', code: 'CN-HN', name: 'Chi nhánh', levelType: 'BRANCH' },
    { id: 'id-sales', code: 'CN-HN-KD', name: 'Phòng Kinh doanh', levelType: 'DEPARTMENT' },
    { id: 'id-teller', code: 'CN-HN-GD', name: 'Phòng Giao dịch', levelType: 'DEPARTMENT' },
  ];
  const page = (list = [memberFixture()]) => ({
    items: list,
    totalCount: list.length,
    page: 1,
    pageSize: 20,
  });
  let query: Record<string, string>;

  beforeEach(async () => {
    query = {};
    for (const spy of Object.values(api)) spy.calls.reset();
    api.overview.and.returnValue(new Subject());
    api.members.and.returnValue(new Subject());
    api.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 20 }));
    await TestBed.configureTestingModule({
      imports: [DepartmentViewComponent],
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
      .overrideComponent(DepartmentViewComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
  });

  const openWithData = (overview = overviewFixture()) => {
    api.list.and.returnValue(of({ items, totalCount: 3, page: 1, pageSize: 100 }));
    api.overview.and.returnValue(of(overview));
    api.members.and.returnValue(of(page()));
    const fixture = TestBed.createComponent(DepartmentViewComponent);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  };

  it('renders a real empty response and updates text and labels live', () => {
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');
    const fixture = TestBed.createComponent(DepartmentViewComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.textContent).toContain('Chưa có phòng ban');
    expect(root.querySelector('select')?.getAttribute('aria-label')).toBe('Chọn phòng ban');
    i18n.setLanguage('en');
    fixture.detectChanges();
    expect(root.textContent).toContain('No department in your scope');
    expect(root.querySelector('select')?.getAttribute('aria-label')).toBe('Select department');
  });

  it('shows an error and allows a retry without injecting sample records', () => {
    api.list.and.returnValue(throwError(() => new Error('403')));
    TestBed.inject(I18nService).setLanguage('en');
    const fixture = TestBed.createComponent(DepartmentViewComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Could not load');
    api.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 20 }));
    root.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')).toBeNull();
  });

  it('a failing overview shows the error state, never numbers', () => {
    api.list.and.returnValue(of({ items, totalCount: 3, page: 1, pageSize: 100 }));
    api.overview.and.returnValue(throwError(() => new Error('500')));
    api.members.and.returnValue(of(page()));
    TestBed.inject(I18nService).setLanguage('en');
    const fixture = TestBed.createComponent(DepartmentViewComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Could not load');
    expect(root.querySelector('app-perf-metric-card')).toBeNull();
  });

  it('renders the API figures and hides what has no source', () => {
    TestBed.inject(I18nService).setLanguage('en');
    const { root } = openWithData();
    const cards = Array.from(root.querySelectorAll('app-perf-metric-card')).map(
      (c) => c.textContent,
    );
    expect(cards[0]).toContain('81.3'); // average, 1 decimal
    expect(cards[0]).toContain('▲ 1.3 vs Period Before');
    expect(cards[1]).toContain('1/2'); // one of two KPI rows at or above 100 %
    expect(cards[2]).toContain('1/2'); // declined / compared
    expect(cards[3]).toContain('3'); // 2 pending review + 1 overdue, escalated is null
    expect(root.textContent).not.toContain('Escalated');
    expect(root.textContent).toContain('Unit Alpha');
    expect(root.textContent).toContain('Person One');
  });

  it('hides comparison labels when the API has no compare period', () => {
    TestBed.inject(I18nService).setLanguage('en');
    const { root } = openWithData(overviewFixture({ comparePeriod: null }));
    expect(root.textContent).toContain('No comparison period');
    expect(root.textContent).not.toContain('Period Before');
  });

  it('reloads overview and members when the comparison changes, members only for filters', async () => {
    const { fixture, root } = openWithData();
    expect(api.overview).toHaveBeenCalledWith('id-sales', undefined, 'PREVIOUS');
    await fixture.whenStable();
    api.overview.calls.reset();
    api.members.calls.reset();

    const compare = root.querySelectorAll<HTMLSelectElement>('.filters select')[2];
    compare.value = compare.options[1].value;
    compare.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(api.overview).toHaveBeenCalledWith('id-sales', 'p-cur', 'SAME_LAST_YEAR');
    expect(api.members.calls.mostRecent().args[1]).toEqual(
      jasmine.objectContaining({ compare: 'SAME_LAST_YEAR', sort: 'CHANGE_DESC', page: 1 }),
    );

    await fixture.whenStable();
    api.overview.calls.reset();
    const sort = root.querySelector<HTMLSelectElement>('select[name="sort"]')!;
    sort.value = sort.options[1].value;
    sort.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(api.overview).not.toHaveBeenCalled();
    expect(api.members.calls.mostRecent().args[1]).toEqual(
      jasmine.objectContaining({ sort: 'TOTAL_ASC' }),
    );
  });

  it('writes department, period and comparison into the address', () => {
    openWithData();
    expect(TestBed.inject(Location).path()).toContain('department=UNIT-A');
    expect(TestBed.inject(Location).path()).toContain('periodId=p-cur');
    expect(TestBed.inject(Location).path()).toContain('compare=PREVIOUS');
  });

  it('a failing member list keeps the overview and offers a retry', () => {
    const { fixture, root } = openWithData();
    api.members.and.returnValue(throwError(() => new Error('500')));
    const search = root.querySelector<HTMLFormElement>('form.member-filters')!;
    search.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(root.querySelector('#members [role="alert"]')).not.toBeNull();
    expect(root.querySelector('app-perf-metric-card')).not.toBeNull();
  });

  it('exports the overview for the selected period and comparison', () => {
    api.exportOverview.and.returnValue(of(new Blob(['x'])));
    const save = spyOn(HTMLAnchorElement.prototype, 'click');
    const { root } = openWithData();
    root.querySelector<HTMLButtonElement>('.filters button')!.click();
    expect(api.exportOverview).toHaveBeenCalledWith('id-sales', 'p-cur', 'PREVIOUS');
    expect(save).toHaveBeenCalled();
  });

  describe('choosing the department from the link', () => {
    const open = () => {
      api.list.and.returnValue(of({ items, totalCount: 3, page: 1, pageSize: 100 }));
      TestBed.createComponent(DepartmentViewComponent).detectChanges();
    };

    it('selects the department named by ?department=<code> with its period and comparison', () => {
      query = { department: 'CN-HN-GD', periodId: 'p-9', compare: 'SAME_LAST_YEAR' };
      open();
      expect(api.overview).toHaveBeenCalledWith('id-teller', 'p-9', 'SAME_LAST_YEAR');
    });

    it('still honours a link from before URLs used codes (?departmentId=<guid>)', () => {
      query = { departmentId: 'id-teller' };
      open();
      expect(api.overview).toHaveBeenCalledWith('id-teller', undefined, 'PREVIOUS');
    });

    it('ignores an unknown comparison and falls back to the first department', () => {
      query = { department: 'NOPE', compare: 'WEEK' };
      open();
      expect(api.overview).toHaveBeenCalledWith('id-sales', undefined, 'PREVIOUS');
    });
  });
});
