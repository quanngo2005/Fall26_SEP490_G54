import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { BranchTargetOverviewComponent } from './branch-target-overview.component';
import { DepartmentTargetOverviewComponent } from './department-target-overview.component';
import { TargetCellComponent } from './target-cell.component';
import { TargetOverviewComponent } from './target-overview.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}
@Component({ selector: 'app-branch-target-overview', template: 'branch' })
class BranchStub {}
@Component({ selector: 'app-department-target-overview', template: 'department' })
class DepartmentStub {}

describe('TargetOverviewComponent', () => {
  const render = (roles: string[]) => {
    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: { currentUser: () => ({ roles }) } }],
    }).overrideComponent(TargetOverviewComponent, {
      remove: {
        imports: [
          HeaderComponent,
          SidebarComponent,
          BranchTargetOverviewComponent,
          DepartmentTargetOverviewComponent,
        ],
      },
      add: { imports: [HeaderStub, SidebarStub, BranchStub, DepartmentStub] },
    });
    const fixture = TestBed.createComponent(TargetOverviewComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('shows the branch screen to branch managers', () => {
    expect(render(['BRANCH_MANAGER']).querySelector('main')?.textContent).toContain('branch');
  });

  it('shows the department screen to department managers', () => {
    expect(render(['MANAGER']).querySelector('main')?.textContent).toContain('department');
  });
});

describe('TargetCellComponent', () => {
  const render = (value: 'NONE' | 'UNALLOCATED' | number) => {
    const fixture = TestBed.createComponent(TargetCellComponent);
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).textContent?.trim();
  };

  it('renders a dash, the not-allocated marker or the formatted number', () => {
    expect(render('NONE')).toBe('—');
    const i18n = TestBed.inject(I18nService);
    expect(render('UNALLOCATED')).toBe(i18n.localization.targetOverview.unallocated);
    expect(render(1200000)).toBe(i18n.formatNumber(1200000));
  });
});
