import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { CampaignDetail } from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { EmployeeDetail } from '../../core/employee.models';
import { EmployeeService } from '../../core/employee.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { WorkTypeService } from '../../core/work-type.service';
import { CampaignCreateModalComponent } from './campaign-create-modal.component';

const employee = {
  id: 'e1',
  employeeCode: 'NV-1',
  fullName: 'Phạm Thu Hà',
  departmentName: 'KHCN',
};

describe('CampaignCreateModalComponent', () => {
  const campaigns = {
    objectives: jasmine.createSpy('objectives'),
    create: jasmine.createSpy('create'),
  };
  const employees = { list: jasmine.createSpy('list') };
  const workTypes = { list: jasmine.createSpy('list') };

  const open = async () => {
    const fixture = TestBed.createComponent(CampaignCreateModalComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  };

  const type = async (
    fixture: Awaited<ReturnType<typeof open>>,
    selector: string,
    value: string,
  ) => {
    const input = (fixture.nativeElement as HTMLElement).querySelector<
      HTMLInputElement | HTMLSelectElement
    >(selector)!;
    input.value = value;
    input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
    fixture.detectChanges();
    await fixture.whenStable();
  };

  const fillValidForm = async (fixture: Awaited<ReturnType<typeof open>>) => {
    const root: HTMLElement = fixture.nativeElement;
    await type(fixture, '#campaign-code', 'CMP-2026-10');
    await type(fixture, '#campaign-name', 'Kế hoạch mới');
    await type(fixture, '#campaign-objective', 'o1');
    await type(fixture, '#campaign-start', '2026-11-01');
    await type(fixture, '#campaign-end', '2026-12-31');
    root.querySelector<HTMLInputElement>('.check-row input')!.click();
    fixture.componentInstance['addParticipant'](employee as EmployeeDetail);
    fixture.detectChanges();
  };

  const submit = (fixture: Awaited<ReturnType<typeof open>>) => {
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLFormElement>('form')!
      .dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  };

  beforeEach(async () => {
    [campaigns.objectives, campaigns.create, employees.list, workTypes.list].forEach((spy) =>
      spy.calls.reset(),
    );
    campaigns.objectives.and.returnValue(
      of([{ id: 'o1', code: 'OBJ-1', name: 'Tăng trưởng', description: null }]),
    );
    workTypes.list.and.returnValue(
      of({
        items: [
          { id: 'w1', code: 'WT-S01', name: 'Bán thẻ', status: 'ACTIVE' },
          { id: 'w2', code: 'WT-OLD', name: 'Đã ngừng', status: 'INACTIVE' },
        ],
        totalCount: 2,
        page: 1,
        pageSize: 100,
      }),
    );
    employees.list.and.returnValue(of({ items: [employee], totalCount: 1, page: 1, pageSize: 8 }));
    await TestBed.configureTestingModule({
      imports: [CampaignCreateModalComponent],
      providers: [
        { provide: CampaignService, useValue: campaigns },
        { provide: EmployeeService, useValue: employees },
        { provide: WorkTypeService, useValue: workTypes },
      ],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('lists objectives and only active work types', async () => {
    const fixture = await open();
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelectorAll('#campaign-objective option').length).toBe(2);
    expect(root.querySelectorAll('.check-row').length).toBe(1);
    expect(root.textContent).toContain('WT-S01');
    expect(root.textContent).not.toContain('WT-OLD');
  });

  it('does not call the API for an incomplete form and says why', async () => {
    const fixture = await open();

    submit(fixture);

    expect(campaigns.create).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'Vui lòng nhập đủ',
    );
  });

  it('rejects an end date before the start date', async () => {
    const fixture = await open();
    await fillValidForm(fixture);
    await type(fixture, '#campaign-end', '2026-10-01');

    submit(fixture);

    expect(campaigns.create).not.toHaveBeenCalled();
  });

  it('creates the plan with the chosen work types and participants and emits its id', async () => {
    campaigns.create.and.returnValue(of({ id: 'c9' } as CampaignDetail));
    const fixture = await open();
    const created = jasmine.createSpy('created');
    fixture.componentInstance.created.subscribe(created);
    await fillValidForm(fixture);

    submit(fixture);

    expect(campaigns.create).toHaveBeenCalledWith({
      businessObjectiveId: 'o1',
      code: 'CMP-2026-10',
      name: 'Kế hoạch mới',
      description: null,
      startDate: '2026-11-01',
      endDate: '2026-12-31',
      workTypeIds: ['w1'],
      participantEmployeeIds: ['e1'],
    });
    expect(created).toHaveBeenCalledWith('c9');
  });

  it('searches active employees and lets the user add and remove a participant', async () => {
    const fixture = await open();
    const root: HTMLElement = fixture.nativeElement;

    await new Promise((resolve) => setTimeout(resolve, 400));
    fixture.componentInstance['onKeyword']('Hà');
    await new Promise((resolve) => setTimeout(resolve, 400));
    fixture.detectChanges();
    expect(employees.list).toHaveBeenCalledWith({ keyword: 'Hà', status: 'ACTIVE', pageSize: 8 });

    root.querySelector<HTMLButtonElement>('.pick-list button')!.click();
    fixture.detectChanges();
    expect(root.querySelector('.chip')?.textContent).toContain('Phạm Thu Hà');

    root.querySelector<HTMLButtonElement>('.chip button')!.click();
    fixture.detectChanges();
    expect(root.querySelector('.chip')).toBeNull();
  });

  it('shows the server message and stays open when creating fails', async () => {
    campaigns.create.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({ status: 409, error: { message: 'Mã chiến dịch đã tồn tại' } }),
      ),
    );
    const fixture = await open();
    await fillValidForm(fixture);

    submit(fixture);

    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'Mã chiến dịch đã tồn tại',
    );
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBeFalse();
  });

  it('closes from the cancel button unless a save is running', async () => {
    const fixture = await open();
    const closed = jasmine.createSpy('closed');
    fixture.componentInstance.closed.subscribe(closed);

    fixture.nativeElement.querySelector('.modal-footer .button-secondary').click();

    expect(closed).toHaveBeenCalledTimes(1);
  });
});
