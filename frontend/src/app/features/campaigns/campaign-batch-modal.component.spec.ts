import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { BatchWorkResult, CampaignDetail } from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { CampaignBatchModalComponent } from './campaign-batch-modal.component';

const participant = (id: string, name: string, assigned: number) => ({
  employeeId: id,
  employeeCode: id.toUpperCase(),
  fullName: name,
  jobPositionTitle: 'Sales Officer',
  departmentName: 'KHCN',
  assignedTasksCount: assigned,
  completedTasksCount: 0,
});

const campaign = {
  id: 'c1',
  code: 'CMP-2026-07',
  startDate: '2026-10-01',
  endDate: '2026-11-30',
  workTypes: [
    { workTypeId: 'w1', code: 'WT-S01', name: 'Bán thẻ', targetUnit: 'tài khoản' },
    { workTypeId: 'w2', code: 'WT-S02', name: 'Gặp gỡ', targetUnit: 'tỷ VND' },
    { workTypeId: 'w3', code: 'WT-S03', name: 'Hỗ trợ', targetUnit: null },
  ],
  participants: [participant('e1', 'Phạm Thu Hà', 9), participant('e2', 'Trịnh Thu Trang', 4)],
} as unknown as CampaignDetail;

const result: BatchWorkResult = {
  campaignId: 'c1',
  campaignCode: 'CMP-2026-07',
  totalGenerated: 12,
  participantCount: 2,
  message: 'Đã tạo 12 công việc',
};

describe('CampaignBatchModalComponent', () => {
  const api = { batchGenerateWork: jasmine.createSpy('batchGenerateWork') };

  const open = async () => {
    const fixture = TestBed.createComponent(CampaignBatchModalComponent);
    fixture.componentRef.setInput('campaign', campaign);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  };

  const setValue = async (
    fixture: Awaited<ReturnType<typeof open>>,
    selector: string,
    value: string,
  ) => {
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
  };

  const submit = (fixture: Awaited<ReturnType<typeof open>>) => {
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLFormElement>('form')!
      .dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  };

  beforeEach(async () => {
    api.batchGenerateWork.calls.reset();
    await TestBed.configureTestingModule({
      imports: [CampaignBatchModalComponent],
      providers: [{ provide: CampaignService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('starts with every participant selected and the campaign end date as deadline', async () => {
    const fixture = await open();
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelectorAll<HTMLInputElement>('tbody input:checked').length).toBe(2);
    expect(root.querySelector<HTMLInputElement>('#batch-deadline')?.value).toBe('2026-11-30');
    expect(root.querySelector('.modal-footer')?.textContent).toContain('1 đầu việc');
    expect(root.querySelector('.modal-footer')?.textContent).toContain('2 nhân viên');
  });

  it('keeps the footer summary in step with the work count and the selection', async () => {
    const fixture = await open();
    const root: HTMLElement = fixture.nativeElement;

    await setValue(fixture, '#batch-total', '36');
    root.querySelector<HTMLInputElement>('tbody input')!.click();
    fixture.detectChanges();

    expect(root.querySelector('.modal-footer')?.textContent).toContain('36 đầu việc');
    expect(root.querySelector('.modal-footer')?.textContent).toContain('1 nhân viên');
  });

  it('does not call the API without a lead pool source', async () => {
    const fixture = await open();

    submit(fixture);

    expect(api.batchGenerateWork).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'Chọn loại công việc',
    );
  });

  it('does not call the API with more than 1000 works or nobody selected', async () => {
    const fixture = await open();
    await setValue(fixture, '#batch-pool', 'Pool');
    await setValue(fixture, '#batch-total', '1001');
    submit(fixture);
    expect(api.batchGenerateWork).not.toHaveBeenCalled();

    await setValue(fixture, '#batch-total', '10');
    fixture.nativeElement
      .querySelectorAll('tbody input')
      .forEach((checkbox: HTMLInputElement) => checkbox.click());
    submit(fixture);
    expect(api.batchGenerateWork).not.toHaveBeenCalled();
  });

  it('generates the works for the selected participants and emits the result', async () => {
    api.batchGenerateWork.and.returnValue(of(result));
    const fixture = await open();
    const generated = jasmine.createSpy('generated');
    fixture.componentInstance.generated.subscribe(generated);
    await setValue(fixture, '#batch-pool', ' Pool KH đến hạn ');
    await setValue(fixture, '#batch-total', '12');
    fixture.nativeElement.querySelectorAll('tbody input')[1].click();

    submit(fixture);

    expect(api.batchGenerateWork).toHaveBeenCalledWith('c1', {
      workTypeId: 'w1',
      leadPoolSource: 'Pool KH đến hạn',
      allocationRule: 'ROUND_ROBIN',
      totalLeadsCount: 12,
      plannedEndDate: '2026-11-30',
      targetEmployeeIds: ['e1'],
      targetValue: null,
    });
    expect(generated).toHaveBeenCalledWith(result);
  });

  it('shows the server message and allows another attempt when generating fails', async () => {
    api.batchGenerateWork.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({ status: 400, error: { message: 'Không có người nhận việc' } }),
      ),
    );
    const fixture = await open();
    await setValue(fixture, '#batch-pool', 'Pool');
    await setValue(fixture, '#batch-total', '5');

    submit(fixture);

    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'Không có người nhận việc',
    );
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBeFalse();
  });
  it('names the KPI unit of the selected work type next to the expected result', async () => {
    const fixture = await open();
    const root: HTMLElement = fixture.nativeElement;
    const label = () => root.querySelector('label[for="batch-target"]')?.textContent ?? '';
    expect(label()).toContain('(tài khoản)');

    const select = root.querySelector<HTMLSelectElement>('#batch-work-type')!;
    select.value = 'w2';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(label()).toContain('(tỷ VND)');

    select.value = 'w3';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(label()).not.toContain('(tài khoản)');
    expect(label()).not.toContain('(tỷ VND)');
  });

  it('summarizes the expected result with its average per selected person', async () => {
    const fixture = await open();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.modal-footer')?.textContent).not.toContain('Mức mong muốn');

    await setValue(fixture, '#batch-target', '600');

    const footer = root.querySelector('.modal-footer')?.textContent ?? '';
    expect(footer).toContain('Mức mong muốn:');
    expect(footer).toContain('600 tài khoản');
    expect(footer).toContain('bình quân 300 tài khoản/người');
  });

  it('sends the expected result with the generated works', async () => {
    api.batchGenerateWork.and.returnValue(of(result));
    const fixture = await open();
    await setValue(fixture, '#batch-pool', 'Pool');
    await setValue(fixture, '#batch-total', '12');
    await setValue(fixture, '#batch-target', '600');

    submit(fixture);

    expect(api.batchGenerateWork.calls.mostRecent().args[1].targetValue).toBe(600);
  });

  it('does not call the API with a negative expected result', async () => {
    const fixture = await open();
    await setValue(fixture, '#batch-pool', 'Pool');
    await setValue(fixture, '#batch-target', '-5');

    submit(fixture);

    expect(api.batchGenerateWork).not.toHaveBeenCalled();
  });
});
