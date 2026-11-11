import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { I18nService } from '../../core/i18n/i18n.service';
import { CampaignSummary } from '../../core/campaign.models';
import { BranchEvaluationPeriodItem } from '../../core/evaluation.models';
import { PerfAttentionComponent } from './performance-attention.component';
import { PerfCampaignPaceComponent } from './performance-campaign-pace.component';
import { PerfChildUnitsComponent } from './performance-child-units.component';
import { PerfEvaluationPeriodComponent } from './performance-evaluation-period.component';
import { memberFixture, overviewFixture } from './performance-fixtures.spec';
import { PerfKpiBelowPlanComponent } from './performance-kpi-below-plan.component';
import { PerfKpiGroupTableComponent } from './performance-kpi-group-table.component';
import { PerfMemberTableComponent } from './performance-member-table.component';
import { PerfMetricCardComponent } from './performance-metric-card.component';
import { PerfProfileGroupsComponent } from './performance-profile-groups.component';
import { PerfTrendChartComponent } from './performance-trend-chart.component';
import { PerfWorkProgressComponent } from './performance-work-progress.component';

function render<T>(type: Type<T>, inputs: Record<string, unknown>): ComponentFixture<T> {
  const fixture = TestBed.createComponent(type);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  fixture.detectChanges();
  return fixture;
}

const text = (fixture: ComponentFixture<unknown>): string =>
  (fixture.nativeElement as HTMLElement).textContent ?? '';

describe('performance dashboard cards', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    TestBed.inject(I18nService).setLanguage('en');
  });

  it('metric card emits activate on click and Enter only when clickable', () => {
    const fixture = render(PerfMetricCardComponent, {
      label: 'Label',
      value: '2/6',
      clickable: true,
    });
    let count = 0;
    fixture.componentInstance.activate.subscribe(() => count++);
    const card: HTMLElement = fixture.nativeElement.querySelector('article');
    card.click();
    card.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(count).toBe(2);
    expect(card.getAttribute('role')).toBe('link');
    expect(card.classList).toContain('is-clickable');

    fixture.componentRef.setInput('clickable', false);
    fixture.detectChanges();
    card.click();
    expect(count).toBe(2);
    expect(card.getAttribute('role')).toBeNull();
  });

  it('KPI below plan lists only KPIs under 100 % with the API change', () => {
    const data = overviewFixture();
    const fixture = render(PerfKpiBelowPlanComponent, {
      groups: data.performance.kpiGroups,
      compareName: 'Period Before',
    });
    expect(text(fixture)).toContain('Indicator One');
    expect(text(fixture)).not.toContain('Indicator Two');
    expect(text(fixture)).toContain('▼ 5 vs Period Before');
    expect(text(fixture)).toContain('View 2 KPIs');

    fixture.componentRef.setInput('compareName', null);
    fixture.detectChanges();
    expect(text(fixture)).not.toContain('Period Before');
  });

  it('KPI below plan says every KPI is on plan, or that there is none', () => {
    const fixture = render(PerfKpiBelowPlanComponent, { groups: [] });
    expect(text(fixture)).toContain('No KPI target');
  });

  it('trend chart draws one bar per period and an axis from the data', () => {
    const data = overviewFixture();
    const fixture = render(PerfTrendChartComponent, {
      points: data.performance.trend,
      title: 'Trend',
    });
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelectorAll('rect').length).toBe(2);
    expect(root.querySelector('rect.current')).not.toBeNull();
    expect(text(fixture)).toContain('Trend · 2 periods');
    expect(text(fixture)).toContain('79'); // axis starts one unit under the lowest average
    expect(text(fixture)).toContain('83'); // and ends one unit above the highest

    fixture.componentRef.setInput('points', [{ periodId: 'x', periodName: 'X', average: null }]);
    fixture.detectChanges();
    expect(root.querySelector('svg')).toBeNull();
  });

  it('KPI group table shows weights, the campaign column and hides ± without a compare period', () => {
    const data = overviewFixture();
    const fixture = render(PerfKpiGroupTableComponent, {
      groups: data.performance.kpiGroups,
      compareName: 'Period Before',
      showCampaign: true,
    });
    expect(text(fixture)).toContain('Group One');
    expect(text(fixture)).toContain('Role X 60%');
    expect(text(fixture)).toContain('≤ 1.0 %');
    expect(text(fixture)).toContain('20.0 u');
    expect(text(fixture)).toContain('± Period Before');

    fixture.componentRef.setInput('compareName', null);
    fixture.componentRef.setInput('showCampaign', false);
    fixture.detectChanges();
    expect(text(fixture)).not.toContain('±');
    expect(text(fixture)).not.toContain('Campaign');
  });

  it('KPI group table labels a group without name as Other', () => {
    const group = { ...overviewFixture().performance.kpiGroups[0], name: null, weights: [] };
    const fixture = render(PerfKpiGroupTableComponent, { groups: [group] });
    expect(text(fixture)).toContain('Other');
  });

  it('work progress hides the escalated tile while the API has no source', () => {
    const data = overviewFixture();
    const fixture = render(PerfWorkProgressComponent, {
      operational: data.operational,
      workMix: data.workMix,
    });
    expect(text(fixture)).not.toContain('Escalated');
    expect(text(fixture)).toContain('Progress in the period · 10 works');
    expect(text(fixture)).toContain('Work Kind');

    fixture.componentRef.setInput('operational', { ...data.operational, escalated: 3 });
    fixture.detectChanges();
    expect(text(fixture)).toContain('Escalated');
  });

  it('attention card shows the API reasons as rows with links to the employee', () => {
    const data = overviewFixture();
    const fixture = render(PerfAttentionComponent, {
      members: data.attentionMembers,
      employeeCount: 3,
      periodName: 'Period Current',
      compareName: 'Period Before',
    });
    const root: HTMLElement = fixture.nativeElement;
    expect(text(fixture)).toContain('Person One');
    expect(text(fixture)).toContain('Group One 61.0');
    expect(text(fixture)).toContain('▼ 4.5');
    expect(root.querySelector('a.row-link')?.getAttribute('href')).toBe('/employee-detail/E-1');

    fixture.componentRef.setInput('members', []);
    fixture.detectChanges();
    expect(text(fixture)).toContain('No employee needs attention');
  });

  it('profile groups pivot the position groups and flag weak ones', () => {
    const data = overviewFixture();
    const fixture = render(PerfProfileGroupsComponent, { positions: data.performance.positions });
    const root: HTMLElement = fixture.nativeElement;
    expect(text(fixture)).toContain('Group One');
    expect(text(fixture)).toContain('Role X');
    expect(text(fixture)).toContain('60%');
    expect(root.querySelector('.fill.weak')).not.toBeNull();
  });

  it('member table groups rows by position, marks the weakest group and shows disputes', () => {
    const data = overviewFixture();
    const fixture = render(PerfMemberTableComponent, {
      members: [
        memberFixture(),
        memberFixture({
          id: 'emp-2',
          employeeCode: 'E-2',
          fullName: 'Person Two',
          evaluationStatus: 'DISPUTED',
          performanceScore: null,
          changeFromPrevious: null,
          weakestProfileItemId: null,
          groups: [
            {
              profileItemId: 'g-1',
              name: 'Group One',
              displayOrder: 1,
              weightPercent: 60,
              score: null,
            },
          ],
        }),
      ],
      positions: data.performance.positions,
      periodName: 'Period Current',
      compareName: 'Period Before',
    });
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelectorAll('.position-row').length).toBe(1);
    expect(text(fixture)).toMatch(/Role X\s+· 3 employees/);
    expect(root.querySelector('td.down')?.textContent?.trim()).toBe('61.0');
    expect(text(fixture)).toContain('Disputed');
    expect(text(fixture)).toContain('−4.5');
    expect(root.querySelector('a.view')?.getAttribute('href')).toBe('/employee-detail/E-1');

    fixture.componentRef.setInput('compareName', null);
    fixture.detectChanges();
    expect(text(fixture)).not.toContain('Change vs');
  });

  it('child units link to the department screen with the same period and comparison', () => {
    const fixture = render(PerfChildUnitsComponent, {
      units: [
        {
          id: 'u-1',
          code: 'U1',
          name: 'Unit One',
          levelType: 'DEPARTMENT',
          employeeCount: 10,
          evaluatedEmployeeCount: 9,
          average: 79.4,
          previousAverage: 82.7,
          changeFromPrevious: -3.3,
        },
      ],
      periodName: 'Period Current',
      periodId: 'p-cur',
      compareName: 'Period Before',
      compare: 'SAME_LAST_YEAR',
    });
    const href = fixture.nativeElement.querySelector('a.unit').getAttribute('href');
    expect(href).toContain('/department-performance?department=U1');
    expect(href).toContain('periodId=p-cur');
    expect(href).toContain('compare=SAME_LAST_YEAR');
    expect(text(fixture)).toContain('9/10 scored');
    expect(text(fixture)).toContain('▼ 3.3');
  });

  it('evaluation period card follows the newest period that is not locked', () => {
    const period = (patch: Partial<BranchEvaluationPeriodItem>) =>
      ({
        id: 'x',
        code: 'X',
        name: 'X',
        startDate: '2026-01-01',
        endDate: '2026-03-31',
        periodType: 'QUARTER',
        status: 'OPEN',
        displayStatus: 'IN_REVIEW',
        qualityRatio: null,
        submittedDepartmentCount: 3,
        approvedDepartmentCount: 2,
        totalDepartmentCount: 4,
        appealWindow: null,
        canOpen: true,
        canLock: false,
        canViewHistory: true,
        ...patch,
      }) as BranchEvaluationPeriodItem;
    const fixture = render(PerfEvaluationPeriodComponent, {
      periods: [
        period({ id: 'a', name: 'Older', startDate: '2026-04-01' }),
        period({
          id: 'b',
          name: 'Locked newest',
          startDate: '2026-10-01',
          displayStatus: 'LOCKED',
        }),
        period({ id: 'c', name: 'Newest open', startDate: '2026-07-01' }),
      ],
    });
    const root: HTMLElement = fixture.nativeElement;
    expect(text(fixture)).toContain('Newest open');
    expect(text(fixture)).toContain('3/4 departments submitted · 2 departments approved');
    expect(root.querySelector('li.current')?.textContent).toContain('Ranking under review');
    expect(root.querySelectorAll('li.done').length).toBe(2);

    fixture.componentRef.setInput('failed', true);
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('campaign card shows API progress, elapsed marker and pace', () => {
    const campaign = {
      id: 'c-1',
      code: 'C1',
      name: 'Campaign One',
      generatedWorksCount: 4,
      completedWorksCount: 1,
      progressPercent: 25,
      timeElapsedPercent: 60,
      paceGap: -35,
      kpiName: 'Indicator One',
      targetUnit: 'u',
      targetValue: 40,
      actualValue: 7,
    } as CampaignSummary;
    const fixture = render(PerfCampaignPaceComponent, { campaigns: [campaign] });
    const root: HTMLElement = fixture.nativeElement;
    expect(text(fixture)).toContain('Indicator One · 7.0 / 40.0 u');
    expect(text(fixture)).toContain('25%');
    expect(text(fixture)).toContain('▼ 35');
    expect((root.querySelector('.marker') as HTMLElement).style.left).toBe('60%');

    fixture.componentRef.setInput('campaigns', [{ ...campaign, kpiName: null, targetValue: null }]);
    fixture.detectChanges();
    expect(text(fixture)).toContain('1/4 works');
  });
});
