import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DepartmentService } from '../../core/department.service';
import { EmployeeService } from '../../core/employee.service';
import { EvaluationService } from '../../core/evaluation.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { TargetAssignmentService } from '../../core/target-assignment.service';
import { TargetAllocationComponent } from './target-allocation.component';

const paged = <T>(items: T[]) => ({ items, page: 1, pageSize: 100, totalCount: items.length });
const row = (id: string, owner: object, value: number, parent: string | null) => ({
  id,
  kpiDefinitionId: 'k1',
  evaluationPeriodId: 'q4',
  departmentId: null,
  employeeId: null,
  parentAssignmentId: parent,
  targetValue: value,
  status: 'ACTIVE',
  updatedAt: '2026-10-20T00:00:00Z',
  feedbackStatus: null,
  feedbackAt: null,
  ...owner,
});

describe('TargetAllocationComponent', () => {
  const targetApi = {
    list: jasmine.createSpy('list'),
    allocate: jasmine.createSpy('allocate'),
    reallocate: jasmine.createSpy('reallocate'),
    breakdown: jasmine.createSpy('breakdown'),
  };

  beforeEach(() => {
    targetApi.list.and.returnValue(
      of([
        row('p', { departmentId: 'd1' }, 600, null),
        row('c1', { employeeId: 'e1', feedbackStatus: 'COMMENTED' }, 400, 'p'),
        row('c2', { employeeId: 'e2' }, 200, 'p'),
      ]),
    );
    targetApi.reallocate.and.returnValue(of([]));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: TargetAssignmentService, useValue: targetApi },
        {
          provide: EvaluationService,
          useValue: {
            periods: () =>
              of([
                {
                  id: 'q4',
                  code: 'Q4',
                  name: 'Quý 4/2026',
                  periodType: 'QUARTER',
                  startDate: '2026-10-01',
                  endDate: '2026-12-31',
                  status: 'OPEN',
                  isActive: true,
                },
              ]),
          },
        },
        {
          provide: KpiDefinitionService,
          useValue: {
            list: () =>
              of(
                paged([{ id: 'k1', name: 'Huy động vốn', unit: 'tỷ VND', aggregationType: 'SUM' }]),
              ),
          },
        },
        { provide: DepartmentService, useValue: { list: () => of(paged([])) } },
        {
          provide: EmployeeService,
          useValue: {
            me: () => of({ id: 'm', departmentId: 'd1', departmentName: 'Kinh doanh' }),
            list: () =>
              of(
                paged([
                  { id: 'e1', fullName: 'An', jobPositionTitle: 'CV' },
                  { id: 'e2', fullName: 'Bình', jobPositionTitle: 'CV' },
                ]),
              ),
          },
        },
      ],
    });
    TestBed.inject(I18nService).setLanguage('vi');
  });

  const render = async () => {
    const fixture = TestBed.createComponent(TargetAllocationComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  };

  const click = (fixture: { nativeElement: unknown; detectChanges(): void }, selector: string) => {
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(selector)?.click();
    fixture.detectChanges();
  };

  it('keeps an allocated KPI read-only until the manager chooses to adjust it', async () => {
    const fixture = await render();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector<HTMLInputElement>('.cell-input')?.disabled).toBeTrue();

    click(fixture, '#allocation-adjust');
    await fixture.whenStable();

    expect(element.querySelector<HTMLInputElement>('.cell-input')?.disabled).toBeFalse();
    expect(element.querySelector<HTMLButtonElement>('#allocation-save')?.disabled).toBeTrue();
  });

  it('sends every owner with the new values and the reason when an adjustment is saved', async () => {
    const fixture = await render();
    const element = fixture.nativeElement as HTMLElement;
    click(fixture, '#allocation-adjust');

    const [first, second] = Array.from(element.querySelectorAll<HTMLInputElement>('.cell-input'));
    first.value = '300';
    first.dispatchEvent(new Event('input'));
    second.value = '300';
    second.dispatchEvent(new Event('input'));
    const reason = element.querySelector<HTMLInputElement>(
      '.adjust-reason input',
    ) as HTMLInputElement;
    reason.value = 'Theo góp ý';
    reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    click(fixture, '#allocation-save');

    expect(targetApi.reallocate).toHaveBeenCalledWith('p', {
      allocations: [
        { employeeId: 'e1', targetValue: 300 },
        { employeeId: 'e2', targetValue: 300 },
      ],
      changeReason: 'Theo góp ý',
    });
  });

  it('blocks saving an adjustment that does not add up to the department target', async () => {
    const fixture = await render();
    const element = fixture.nativeElement as HTMLElement;
    click(fixture, '#allocation-adjust');

    const [first, second] = Array.from(element.querySelectorAll<HTMLInputElement>('.cell-input'));
    first.value = '100';
    first.dispatchEvent(new Event('input'));
    second.value = '100';
    second.dispatchEvent(new Event('input'));
    const reason = element.querySelector<HTMLInputElement>(
      '.adjust-reason input',
    ) as HTMLInputElement;
    reason.value = 'x';
    reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(element.querySelector<HTMLButtonElement>('#allocation-save')?.disabled).toBeTrue();
    expect(element.querySelector('.alloc-notice.is-error')).not.toBeNull();
  });
});

describe('TargetAllocationComponent with a team', () => {
  const targetApi = {
    list: jasmine.createSpy('list'),
    allocate: jasmine.createSpy('allocate'),
    reallocate: jasmine.createSpy('reallocate'),
  };

  beforeEach(() => {
    targetApi.list.and.returnValue(
      of([
        row('p', { departmentId: 'd1' }, 600, null),
        row('tp', { departmentId: 't1' }, 600, 'p'),
      ]),
    );
    targetApi.allocate.and.returnValue(of([]));
    targetApi.reallocate.and.returnValue(of([]));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: TargetAssignmentService, useValue: targetApi },
        {
          provide: EvaluationService,
          useValue: {
            periods: () =>
              of([
                {
                  id: 'q4',
                  code: 'Q4',
                  name: 'Quý 4/2026',
                  periodType: 'QUARTER',
                  startDate: '2026-10-01',
                  endDate: '2026-12-31',
                  status: 'OPEN',
                  isActive: true,
                },
              ]),
          },
        },
        {
          provide: KpiDefinitionService,
          useValue: {
            list: () =>
              of(
                paged([{ id: 'k1', name: 'Huy động vốn', unit: 'tỷ VND', aggregationType: 'SUM' }]),
              ),
          },
        },
        {
          provide: DepartmentService,
          useValue: {
            list: () =>
              of(paged([{ id: 't1', name: 'Nhóm 1', parentDepartmentId: 'd1', employeeCount: 2 }])),
          },
        },
        {
          provide: EmployeeService,
          useValue: {
            me: () => of({ id: 'm', departmentId: 'd1', departmentName: 'Kinh doanh' }),
            list: (filter: { departmentId: string }) =>
              of(
                paged(
                  filter.departmentId === 't1'
                    ? [
                        { id: 'e1', fullName: 'An', jobPositionTitle: 'CV' },
                        { id: 'e2', fullName: 'Bình', jobPositionTitle: 'CV' },
                      ]
                    : [{ id: 'e0', fullName: 'Chi', jobPositionTitle: 'CV' }],
                ),
              ),
          },
        },
      ],
    });
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('shows a team as the total of its employees', async () => {
    const fixture = TestBed.createComponent(TargetAllocationComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('#allocation-adjust')?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(element.querySelector('.team-row .team-total')?.textContent).toContain('600');
    expect(element.querySelector('.team-row input')).toBeNull();
  });

  it('splits the department target between every employee, then each team total among its employees', async () => {
    const fixture = TestBed.createComponent(TargetAllocationComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('#allocation-adjust')?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const fill = (selector: string, value: string) => {
      const input = element.querySelector<HTMLInputElement>(selector) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    fill('[aria-label^="Chi"]', '100');
    const [first, second] = Array.from(
      element.querySelectorAll<HTMLInputElement>('.member-row .cell-input'),
    );
    first.value = '250';
    first.dispatchEvent(new Event('input'));
    second.value = '250';
    second.dispatchEvent(new Event('input'));
    fill('.adjust-reason input', 'Theo góp ý');
    fixture.detectChanges();
    element.querySelector<HTMLButtonElement>('#allocation-save')?.click();

    expect(targetApi.reallocate).toHaveBeenCalledWith('p', {
      allocations: [
        { employeeId: 'e0', targetValue: 100 },
        { departmentId: 't1', targetValue: 500 },
      ],
      changeReason: 'Theo góp ý',
    });
    expect(targetApi.allocate).toHaveBeenCalledWith('tp', {
      allocations: [
        { employeeId: 'e1', targetValue: 250 },
        { employeeId: 'e2', targetValue: 250 },
      ],
    });
  });

  const adjusting = async () => {
    const fixture = TestBed.createComponent(TargetAllocationComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('#allocation-adjust')?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const type = (input: HTMLInputElement, value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { element, type, settle, fixture };
  };

  it('gives the rest of a team what the edited employee no longer holds', async () => {
    const { element, type, settle } = await adjusting();
    const [first, second] = Array.from(
      element.querySelectorAll<HTMLInputElement>('.member-row .cell-input'),
    );

    type(first, '200');
    await settle();

    expect(second.value).toBe('400');
    expect(element.querySelector('.team-row .team-total')?.textContent).toContain('600');
    expect(element.querySelectorAll('.member-row .pin').length).toBe(1);
  });

  it('balances every cell that is not pinned across teams, keeping the pinned ones', async () => {
    const { element, type, settle } = await adjusting();
    type(element.querySelector<HTMLInputElement>('[aria-label^="Chi"]') as HTMLInputElement, '100');
    await settle();

    element.querySelector<HTMLButtonElement>('#allocation-balance')?.click();
    await settle();

    const values = Array.from(element.querySelectorAll<HTMLInputElement>('.cell-input')).map(
      (input) => input.value,
    );
    expect(values).toEqual(['100', '250', '250']);
    expect(element.querySelector('.alloc-notice')?.textContent).toContain('giữ nguyên 1 ô đã ghim');
  });

  it('unpins a cell and balancing then moves it too', async () => {
    const { element, type, settle } = await adjusting();
    type(element.querySelector<HTMLInputElement>('[aria-label^="Chi"]') as HTMLInputElement, '100');
    await settle();

    element.querySelector<HTMLButtonElement>('.pin')?.click();
    await settle();
    element.querySelector<HTMLButtonElement>('#allocation-balance')?.click();
    await settle();

    const values = Array.from(element.querySelectorAll<HTMLInputElement>('.cell-input')).map(
      (input) => input.value,
    );
    expect(values).toEqual(['200', '200', '200']);
  });
});
