import { HttpErrorResponse } from '@angular/common/http';
import { ComponentRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { SubmissionContext } from '../../core/result-submission.models';
import { ResultSubmissionService } from '../../core/result-submission.service';
import { WorkDetail } from '../../core/work.models';
import {
  fileExtension,
  parseTransactionCodes,
  ResultSubmitted,
  SubmitResultDialogComponent,
} from './submit-result-dialog.component';

const work = {
  id: 'w1',
  code: 'WK-1038',
  title: 'E-banking - Đăng ký',
  status: 'InProgress',
  plannedStartDate: '2026-10-24',
  plannedEndDate: '2026-10-25',
} as unknown as WorkDetail;

const context: SubmissionContext = {
  workId: 'w1',
  workCode: 'WK-1038',
  workTitle: 'E-banking - Đăng ký',
  workStatus: 'IN_PROGRESS',
  assigneeEmployeeId: 'e1',
  assigneeEmployeeName: 'Nguyễn Văn A',
  assigneeEmployeeCode: 'NV001',
  workTypeId: 't1',
  workTypeName: 'E-banking',
  nextSubmissionNo: 1,
  previousSubmissionsCount: 0,
  kpiCriteria: [
    {
      kpiDefinitionId: 'k1',
      code: 'KPI-EBK-P',
      name: 'Số tài khoản mới',
      description: null,
      unit: 'tài khoản',
      aggregationType: 'SUM',
      direction: 'HIGHER_IS_BETTER',
      isRequired: true,
      formula: null,
    },
    {
      kpiDefinitionId: 'k2',
      code: 'KPI-OPT',
      name: 'Chỉ số tùy chọn',
      description: null,
      unit: 'điểm',
      aggregationType: 'SUM',
      direction: 'HIGHER_IS_BETTER',
      isRequired: false,
      formula: null,
    },
    {
      kpiDefinitionId: 'k3',
      code: 'KPI-RATIO',
      name: 'Tỷ lệ thành công',
      description: null,
      unit: '%',
      aggregationType: 'RATIO',
      direction: 'HIGHER_IS_BETTER',
      isRequired: true,
      formula: null,
    },
  ],
  allowedFileTypes: ['.pdf', '.jpg', '.png'],
  maxFileSizeBytes: 10 * 1024 * 1024,
  maxFileCount: 2,
};

describe('parseTransactionCodes', () => {
  it('splits on commas, semicolons, spaces and line breaks, trims and drops duplicates', () => {
    expect(parseTransactionCodes(' TXN-1, TXN-2;TXN-1\nTXN-3  ')).toEqual([
      'TXN-1',
      'TXN-2',
      'TXN-3',
    ]);
    expect(parseTransactionCodes('   ')).toEqual([]);
  });

  it('reads the extension in lower case', () => {
    expect(fileExtension('Phieu.PDF')).toBe('.pdf');
    expect(fileExtension('noext')).toBe('');
  });
});

describe('SubmitResultDialogComponent', () => {
  const api = {
    getSubmissionContext: jasmine.createSpy('getSubmissionContext'),
    createSubmission: jasmine.createSpy('createSubmission'),
    addTransactionCodes: jasmine.createSpy('addTransactionCodes'),
  };
  let fixture: ComponentFixture<SubmitResultDialogComponent>;
  let element: HTMLElement;
  let outcomes: ResultSubmitted[];
  let closes: number;

  type Internals = {
    codesText: string;
    result: string;
    files: { set(files: File[]): void };
    setMetric(index: number, field: 'value' | 'denominator', text: string): void;
    submit(): void;
    onPick(event: Event): void;
  };
  const internals = () => fixture.componentInstance as unknown as Internals;

  async function setup(ctx: SubmissionContext | 'error' = context): Promise<void> {
    api.getSubmissionContext.calls.reset();
    api.createSubmission.calls.reset();
    api.addTransactionCodes.calls.reset();
    api.getSubmissionContext.and.returnValue(
      ctx === 'error' ? throwError(() => new HttpErrorResponse({ status: 500 })) : of(ctx),
    );
    api.createSubmission.and.returnValue(
      of({ submissionId: 's1', workId: 'w1', status: 'SUBMITTED' }),
    );
    api.addTransactionCodes.and.returnValue(
      of({ workId: 'w1', workStatus: 'SUBMITTED', items: [] }),
    );
    outcomes = [];
    closes = 0;
    await TestBed.configureTestingModule({
      imports: [SubmitResultDialogComponent],
      providers: [{ provide: ResultSubmissionService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    fixture = TestBed.createComponent(SubmitResultDialogComponent);
    const ref: ComponentRef<SubmitResultDialogComponent> = fixture.componentRef;
    ref.setInput('work', work);
    fixture.componentInstance.submittedResult.subscribe((o) => outcomes.push(o));
    fixture.componentInstance.closed.subscribe(() => closes++);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const fill = (codes: string, result: string) => {
    internals().codesText = codes;
    internals().result = result;
  };
  const submit = () => {
    internals().submit();
    fixture.detectChanges();
  };
  const pdf = (name = 'phieu.pdf', size = 240 * 1024) =>
    new File([new Uint8Array(size)], name, { type: 'application/pdf' });
  const errors = () =>
    Array.from(element.querySelectorAll('.field-error, .form-error')).map((e) =>
      (e.textContent ?? '').trim(),
    );

  it('shows the title, the work type and version, and the due date', async () => {
    await setup();

    expect(element.querySelector('#submit-title')?.textContent).toContain('Nộp kết quả · WK-1038');
    expect(element.querySelector('.dialog-head p')?.textContent).toContain(
      'E-banking · Phiên bản 1',
    );
    expect(element.querySelector('.deadline')?.textContent).toContain('nộp');
  });

  it('takes the file limits from the server and shows them in the drop zone', async () => {
    await setup();

    expect(element.querySelector('.drop-zone')?.textContent).toContain(
      'PDF, JPG, PNG tối đa 10 MB',
    );
    expect(element.querySelector<HTMLInputElement>('input[type="file"]')?.accept).toBe(
      '.pdf,.jpg,.png',
    );
  });

  it('asks only for the required KPIs, with a denominator for a ratio', async () => {
    await setup();

    expect(element.querySelectorAll('.metric-row').length).toBe(2);
    expect(element.textContent).toContain('KPI-EBK-P');
    expect(element.textContent).toContain('KPI-RATIO');
    expect(element.textContent).not.toContain('KPI-OPT');
    expect(element.querySelector('#metric-den-1')).not.toBeNull();
    expect(element.querySelector('#metric-den-0')).toBeNull();
  });

  it('hides the KPI block when the work type has no required KPI', async () => {
    await setup({ ...context, kpiCriteria: [] });

    expect(element.querySelector('.metrics-box')).toBeNull();
  });

  it('does not submit without a result and tells the employee what is missing', async () => {
    await setup({ ...context, kpiCriteria: [] });
    fill('TXN-1', '   ');

    submit();

    expect(api.createSubmission).not.toHaveBeenCalled();
    expect(errors()).toContain('Bạn nhập kết quả đã làm giúp mình.');
  });

  it('needs a transaction code or an evidence file', async () => {
    await setup({ ...context, kpiCriteria: [] });
    fill('', 'Đã đăng ký xong.');

    submit();

    expect(api.createSubmission).not.toHaveBeenCalled();
    expect(errors().join(' ')).toContain('mã giao dịch hoặc đính kèm minh chứng');
  });

  it('rejects more than ten transaction codes', async () => {
    await setup({ ...context, kpiCriteria: [] });
    fill(Array.from({ length: 11 }, (_, i) => `TXN-${i}`).join(','), 'Xong.');

    submit();

    expect(api.createSubmission).not.toHaveBeenCalled();
    expect(errors().join(' ')).toContain('tối đa 10 mã giao dịch');
  });

  it('requires every required KPI value, and a positive denominator for a ratio', async () => {
    await setup();
    fill('TXN-1', 'Xong.');

    submit();
    expect(api.createSubmission).not.toHaveBeenCalled();
    expect(errors().length).toBeGreaterThan(0);

    internals().setMetric(0, 'value', '5');
    internals().setMetric(1, 'value', '8');
    internals().setMetric(1, 'denominator', '0');
    submit();
    expect(api.createSubmission).not.toHaveBeenCalled();

    internals().setMetric(1, 'denominator', '10');
    submit();
    expect(api.createSubmission).toHaveBeenCalledTimes(1);
  });

  it('sends the result, KPI values and files, then attaches the transaction codes', async () => {
    await setup();
    fill('TXN-88342, TXN-88343', '  Đã đăng ký xong.  ');
    internals().setMetric(0, 'value', '5');
    internals().setMetric(1, 'value', '8');
    internals().setMetric(1, 'denominator', '10');
    internals().files.set([pdf()]);

    submit();
    await fixture.whenStable();

    const [workId, form] = api.createSubmission.calls.mostRecent().args as [string, FormData];
    expect(workId).toBe('w1');
    expect(form.get('comment')).toBe('Đã đăng ký xong.');
    expect(JSON.parse(form.get('metricItemsJson') as string)).toEqual([
      { kpiDefinitionId: 'k1', value: 5 },
      { kpiDefinitionId: 'k3', value: 8, denominator: 10 },
    ]);
    expect((form.getAll('files') as File[]).map((f) => f.name)).toEqual(['phieu.pdf']);
    expect(api.addTransactionCodes).toHaveBeenCalledWith('w1', ['TXN-88342', 'TXN-88343']);
    expect(outcomes).toEqual([{ submissionId: 's1', codesFailed: false }]);
  });

  it('submits evidence alone without calling the transactions endpoint', async () => {
    await setup({ ...context, kpiCriteria: [] });
    fill('', 'Xong.');
    internals().files.set([pdf()]);

    submit();
    await fixture.whenStable();

    expect(api.createSubmission).toHaveBeenCalledTimes(1);
    expect(api.addTransactionCodes).not.toHaveBeenCalled();
    expect(outcomes[0].codesFailed).toBeFalse();
  });

  it('keeps the saved result and reports it when the codes cannot be attached', async () => {
    await setup({ ...context, kpiCriteria: [] });
    api.addTransactionCodes.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409 })),
    );
    fill('TXN-1', 'Xong.');

    submit();
    await fixture.whenStable();

    expect(outcomes).toEqual([{ submissionId: 's1', codesFailed: true }]);
  });

  it('shows the first server validation message for a 400', async () => {
    await setup({ ...context, kpiCriteria: [] });
    api.createSubmission.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: ['MetricItems: Tiêu chí KPI bắt buộc chưa được nhập.'] },
          }),
      ),
    );
    fill('TXN-1', 'Xong.');

    submit();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(outcomes.length).toBe(0);
    expect(element.querySelector('.form-error')?.textContent?.trim()).toBe(
      'Tiêu chí KPI bắt buộc chưa được nhập.',
    );
    expect(element.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBeFalse();
  });

  it('explains a 403 and a 409 in plain words', async () => {
    await setup({ ...context, kpiCriteria: [] });
    fill('TXN-1', 'Xong.');

    api.createSubmission.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
    submit();
    fixture.detectChanges();
    expect(element.querySelector('.form-error')?.textContent).toContain(
      'Chỉ người được giao việc mới nộp được kết quả.',
    );

    api.createSubmission.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    submit();
    fixture.detectChanges();
    expect(element.querySelector('.form-error')?.textContent).toContain(
      'không còn ở trạng thái nộp được',
    );
  });

  it('refuses files of the wrong type, too large, or beyond the count limit', async () => {
    await setup({ ...context, kpiCriteria: [] });
    const input = document.createElement('input');
    const pick = (files: File[]) => {
      Object.defineProperty(input, 'files', { value: files, configurable: true });
      internals().onPick({ target: input } as unknown as Event);
      fixture.detectChanges();
    };

    pick([new File(['x'], 'macro.exe')]);
    expect(errors().join(' ')).toContain('macro.exe không đúng định dạng');

    pick([pdf('big.pdf', 11 * 1024 * 1024)]);
    expect(errors().join(' ')).toContain('big.pdf lớn hơn 10 MB');

    pick([pdf('a.pdf', 10), pdf('b.pdf', 10), pdf('c.pdf', 10)]);
    expect(element.querySelectorAll('.file-row').length).toBe(2);
    expect(errors().join(' ')).toContain('tối đa 2 tệp');
  });

  it('removes a chosen file from the list', async () => {
    await setup({ ...context, kpiCriteria: [] });
    internals().files.set([pdf('a.pdf'), pdf('b.pdf')]);
    fixture.detectChanges();

    element.querySelector<HTMLButtonElement>('.file-remove')?.click();
    fixture.detectChanges();

    const names = Array.from(element.querySelectorAll('.file-name')).map((n) => n.textContent);
    expect(names).toEqual(['b.pdf']);
  });

  it('closes on the cancel button and on Escape, but not while saving', async () => {
    await setup({ ...context, kpiCriteria: [] });

    element.querySelector<HTMLButtonElement>('button.button-secondary')?.click();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(closes).toBe(2);
  });

  it('shows a friendly message and blocks the submit button when the context cannot load', async () => {
    await setup('error');

    expect(element.querySelector('.form-error')?.textContent).toContain(
      'Chưa tải được thông tin nộp kết quả',
    );
    expect(element.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBeTrue();
  });

  it('marks the due date as late once it has passed', async () => {
    await setup({ ...context, kpiCriteria: [] });

    expect(element.querySelector('.deadline')?.classList.contains('is-late')).toBeTrue();
    expect(element.querySelector('.deadline')?.textContent).toContain('quá hạn');
  });
});
