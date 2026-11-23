import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { DepartmentService } from '../../core/department.service';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationService } from '../../core/evaluation.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { TargetSetup } from '../../core/target-assignment.models';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { BranchTargetOverviewComponent } from './branch-target-overview.component';

const paged = <T>(items: T[]) => ({ items, totalCount: items.length, page: 1, pageSize: 100 });

function setup(status: string): TargetSetup {
  return {
    branchYearId: status === 'NONE' ? null : 'by-1',
    hasBranchYear: status !== 'NONE',
    importedAt: null,
    rowCount: status === 'NONE' ? 0 : 3,
    step: status === 'NONE' ? 'IMPORT' : status === 'ACTIVE' ? 'PUBLISHED' : 'QUARTER',
    status,
    quarters: { done: 0, total: 3 },
    allocation: { done: 0, total: 3 },
    canPublish: false,
    blockers: [],
  };
}

/** G19: "Thiết lập mục tiêu năm" follows the setup state of the selected year. */
describe('BranchTargetOverviewComponent setup button', () => {
  let targetApi: jasmine.SpyObj<TargetAssignmentService>;

  async function render(status: string): Promise<ComponentFixture<BranchTargetOverviewComponent>> {
    targetApi = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'list',
      'getSetup',
    ]);
    targetApi.list.and.returnValue(of([]));
    targetApi.getSetup.and.returnValue(of(setup(status)));

    TestBed.configureTestingModule({
      providers: [
        { provide: TargetAssignmentService, useValue: targetApi },
        {
          provide: AuthService,
          useValue: { currentUser: () => ({ permissions: ['TARGET_MANAGE'] }) },
        },
        {
          provide: EvaluationService,
          useValue: {
            periods: () => of([{ id: 'y', periodType: 'YEAR', startDate: '2026-01-01' }]),
          },
        },
        { provide: KpiDefinitionService, useValue: { list: () => of(paged([])) } },
        {
          provide: DepartmentService,
          useValue: {
            list: () =>
              of(
                paged([
                  { id: 'b', name: 'Chi nhánh', levelType: 'BRANCH', parentDepartmentId: null },
                ]),
              ),
          },
        },
        { provide: EmployeeService, useValue: { list: () => of(paged([])) } },
      ],
    });
    TestBed.inject(I18nService).setLanguage('vi');

    const fixture = TestBed.createComponent(BranchTargetOverviewComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const setupButton = (fixture: ComponentFixture<BranchTargetOverviewComponent>) =>
    Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((button) =>
      ['Thiết lập mục tiêu năm', 'Tiếp tục thiết lập'].includes(button.textContent?.trim() ?? ''),
    );

  afterEach(() => localStorage.clear());

  it('opens the import modal when nothing was imported yet', async () => {
    const fixture = await render('NONE');

    expect(targetApi.getSetup).toHaveBeenCalledWith('b', 2026);
    setupButton(fixture)!.click();
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('app-import-branch-target-modal'),
    ).not.toBeNull();
  });

  it('continues a draft in the setup wizard', async () => {
    const fixture = await render('DRAFT');
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const button = setupButton(fixture)!;
    expect(button.textContent?.trim()).toBe('Tiếp tục thiết lập');
    button.click();

    expect(navigate).toHaveBeenCalledWith(['/target-setup', 2026]);
  });

  const importButton = (fixture: ComponentFixture<BranchTargetOverviewComponent>) =>
    Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((button) => button.textContent?.trim() === 'Nhập chỉ tiêu từ Excel');

  it('always offers the Excel import, locked once the year is published', async () => {
    const draft = await render('DRAFT');
    importButton(draft)!.click();
    draft.detectChanges();
    expect(
      (draft.nativeElement as HTMLElement).querySelector('app-import-branch-target-modal'),
    ).not.toBeNull();

    TestBed.resetTestingModule();
    const published = await render('ACTIVE');
    expect(importButton(published)!.disabled).toBeTrue();
  });

  it('hides the button once the year is published', async () => {
    const fixture = await render('ACTIVE');
    expect(setupButton(fixture)).toBeUndefined();
  });
});
