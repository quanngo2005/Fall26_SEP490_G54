import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { CampaignService } from '../../core/campaign.service';
import { DepartmentService } from '../../core/department.service';
import { EvaluationService } from '../../core/evaluation.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { NavigationService, ROLE_MENUS } from '../../shared/layout/navigation.service';
import { overviewFixture } from '../performance-dashboard/perf-fixtures.spec';
import { BranchPerformanceComponent } from './branch-performance.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

describe('BranchPerformanceComponent', () => {
  const departments = {
    list: jasmine.createSpy('list'),
    overview: jasmine.createSpy('overview'),
    exportOverview: jasmine.createSpy('exportOverview'),
  };
  const evaluations = { branchPeriods: jasmine.createSpy('branchPeriods') };
  const campaigns = { list: jasmine.createSpy('list') };
  const branch = { id: 'br-1', code: 'BR', name: 'Branch One', levelType: 'BRANCH' };
  const overview = overviewFixture({
    childUnits: [
      {
        id: 'dep-1',
        code: 'UNIT-A',
        name: 'Unit Alpha',
        levelType: 'DEPARTMENT',
        employeeCount: 3,
        evaluatedEmployeeCount: 2,
        average: 81.25,
        previousAverage: 82,
        changeFromPrevious: -0.75,
      },
    ],
    metrics: { ...overviewFixture().metrics, declinedChildUnitCount: 1 },
  });

  beforeEach(async () => {
    for (const spy of [...Object.values(departments), evaluations.branchPeriods, campaigns.list]) {
      spy.calls.reset();
    }
    departments.list.and.returnValue(
      of({ items: [branch], totalCount: 1, page: 1, pageSize: 100 }),
    );
    departments.overview.and.returnValue(of(overview));
    evaluations.branchPeriods.and.returnValue(
      of({ branchName: 'Branch One', branchCode: 'BR', availableYears: [2026], periods: [] }),
    );
    campaigns.list.and.returnValue(
      of({
        metrics: {
          totalCampaigns: 3,
          runningCampaigns: 3,
          plannedCampaigns: 0,
          completedCampaigns: 0,
          totalTasksGenerated: 0,
          totalCompletedTasks: 0,
          slowCampaignCount: 2,
        },
        campaigns: { items: [], totalCount: 3, page: 1, pageSize: 5 },
      }),
    );
    await TestBed.configureTestingModule({
      imports: [BranchPerformanceComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DepartmentService, useValue: departments },
        { provide: EvaluationService, useValue: evaluations },
        { provide: CampaignService, useValue: campaigns },
      ],
    })
      .overrideComponent(BranchPerformanceComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('en');
  });

  const open = () => {
    const fixture = TestBed.createComponent(BranchPerformanceComponent);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  };

  it('loads the caller branch, its open period year and running campaigns', () => {
    const { root } = open();
    expect(departments.list).toHaveBeenCalledWith({ levelType: 'BRANCH', pageSize: 100 });
    expect(departments.overview).toHaveBeenCalledWith('br-1', undefined, 'PREVIOUS');
    expect(evaluations.branchPeriods).toHaveBeenCalledWith(2026);
    expect(campaigns.list).toHaveBeenCalledWith(
      jasmine.objectContaining({ status: 'RUNNING', page: 1 }),
    );
    const cards = Array.from(root.querySelectorAll('app-perf-metric-card')).map(
      (c) => c.textContent,
    );
    expect(cards[2]).toContain('1/1'); // declined child units / child units
    expect(cards[3]).toContain('2/3'); // slow campaigns / running campaigns
    expect(root.textContent).toContain('Unit Alpha');
    expect(root.textContent).toContain('Campaign'); // campaign column of the KPI table
  });

  it('keeps the page when a side card fails and offers a retry on it', () => {
    campaigns.list.and.returnValue(throwError(() => new Error('403')));
    evaluations.branchPeriods.and.returnValue(throwError(() => new Error('403')));
    const { root } = open();
    expect(root.textContent).toContain('Could not load campaigns');
    expect(root.textContent).toContain('Could not load evaluation periods');
    expect(root.textContent).toContain('Unit Alpha');
    expect(root.querySelectorAll('app-perf-metric-card')[3].textContent).toContain('—');
  });

  it('shows the error state when the overview fails and an empty state without branch', () => {
    departments.overview.and.returnValue(throwError(() => new Error('500')));
    let view = open();
    expect(view.root.querySelector('[role="alert"]')?.textContent).toContain('Could not load');

    departments.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 100 }));
    view = open();
    expect(view.root.textContent).toContain('No branch in your scope');
  });

  it('opens from the branch performance menu item, and department managers get SCR-PRF-02', () => {
    const item = TestBed.inject(NavigationService).catalog.find(
      (i) => i.id === 'branchPerformance',
    );
    expect(item?.route).toBe('/branch-performance');
    expect(item?.permission).toBe('ORGANIZATION_READ');
    expect(ROLE_MENUS.departmentManager).toContain('departmentPerformance');
    expect(ROLE_MENUS.departmentManager).toContain('teamPerformance');
  });

  it('exports the branch overview', () => {
    departments.exportOverview.and.returnValue(of(new Blob(['x'])));
    spyOn(HTMLAnchorElement.prototype, 'click');
    const { root } = open();
    root.querySelector<HTMLButtonElement>('.filters button')!.click();
    expect(departments.exportOverview).toHaveBeenCalledWith('br-1', 'p-cur', 'PREVIOUS');
  });
});
