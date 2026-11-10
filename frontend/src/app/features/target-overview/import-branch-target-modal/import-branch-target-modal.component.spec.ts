import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { I18nService } from '../../../core/i18n/i18n.service';
import {
  TargetImportCommitResult,
  TargetImportPreview,
  TargetImportRow,
} from '../../../core/target-assignment.models';
import { TargetAssignmentService } from '../../../core/target-assignment.service';
import { ImportBranchTargetModalComponent } from './import-branch-target-modal.component';

describe('ImportBranchTargetModalComponent', () => {
  let fixture: ComponentFixture<ImportBranchTargetModalComponent>;
  let component: ImportBranchTargetModalComponent;
  let targetApi: jasmine.SpyObj<TargetAssignmentService>;

  beforeEach(async () => {
    targetApi = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'importPreview',
      'importCommit',
      'downloadTemplate',
    ]);

    await TestBed.configureTestingModule({
      imports: [ImportBranchTargetModalComponent],
      providers: [{ provide: TargetAssignmentService, useValue: targetApi }],
    }).compileComponents();

    fixture = TestBed.createComponent(ImportBranchTargetModalComponent);
    component = fixture.componentInstance;
    component.branchId = 'b1';
    component.year = 2026;
    fixture.detectChanges();
  });

  function fileInput(bytes: number, name: string): File {
    return new File([new Uint8Array(bytes)], name);
  }

  function changeEventFor(file: File | null): Event {
    const input = document.createElement('input');
    input.type = 'file';
    if (file) {
      Object.defineProperty(input, 'files', { value: [file] });
    }
    return { target: input } as unknown as Event;
  }

  it('starts on the choose-file step with no file selected', () => {
    expect((component as unknown as { step: () => string }).step()).toBe('CHOOSE_FILE');
    expect((component as unknown as { file: () => File | null }).file()).toBeNull();
  });

  it('rejects a non-.xlsx file without calling the API', () => {
    (component as unknown as { onFileChosen: (e: Event) => void }).onFileChosen(
      changeEventFor(fileInput(10, 'plan.pdf')),
    );

    expect((component as unknown as { file: () => File | null }).file()).toBeNull();
    expect((component as unknown as { fileError: () => string | null }).fileError()).toBeTruthy();
    expect(targetApi.importPreview).not.toHaveBeenCalled();
  });

  it('rejects a file over 10 MB', () => {
    (component as unknown as { onFileChosen: (e: Event) => void }).onFileChosen(
      changeEventFor(fileInput(11 * 1024 * 1024, 'big.xlsx')),
    );

    expect((component as unknown as { file: () => File | null }).file()).toBeNull();
    expect((component as unknown as { fileError: () => string | null }).fileError()).toBeTruthy();
  });

  it('accepts a valid file and moves to the preview step on submit', () => {
    const preview = {
      fileName: 'plan.xlsx',
      branchId: 'b1',
      year: 2026,
      summary: { total: 2, valid: 2, invalid: 0, create: 2, update: 0, unchanged: 0 },
      quarterRatios: { q1: null, q2: null, q3: null, q4: null },
      kpisNotInFile: [],
      rows: [],
    };
    targetApi.importPreview.and.returnValue(of(preview));

    (component as unknown as { onFileChosen: (e: Event) => void }).onFileChosen(
      changeEventFor(fileInput(10, 'plan.xlsx')),
    );
    (component as unknown as { submitPreview: () => void }).submitPreview();

    expect(targetApi.importPreview).toHaveBeenCalledWith('b1', 2026, jasmine.any(File));
    expect((component as unknown as { step: () => string }).step()).toBe('PREVIEW');
    expect((component as unknown as { preview: () => unknown }).preview()).toEqual(preview);
  });

  it('shows the load error and stays on the choose-file step when preview fails', () => {
    targetApi.importPreview.and.returnValue(
      throwError(() => ({ error: { errorCode: 'TARGET_IMPORT_YEAR_COLUMN_MISSING' } })),
    );

    (component as unknown as { onFileChosen: (e: Event) => void }).onFileChosen(
      changeEventFor(fileInput(10, 'plan.xlsx')),
    );
    (component as unknown as { submitPreview: () => void }).submitPreview();

    expect((component as unknown as { step: () => string }).step()).toBe('CHOOSE_FILE');
    expect((component as unknown as { loadError: () => string | null }).loadError()).toBeTruthy();
  });

  it('re-uploads the previewed file on commit and moves to the result step', () => {
    const commitResult = {
      branchId: 'b1',
      year: 2026,
      targets: [],
      committed: 2,
      skipped: 0,
      needsReview: false,
    };
    targetApi.importCommit.and.returnValue(of(commitResult));
    (component as unknown as { onFileChosen: (e: Event) => void }).onFileChosen(
      changeEventFor(fileInput(10, 'plan.xlsx')),
    );

    (component as unknown as { commit: () => void }).commit();

    expect(targetApi.importCommit).toHaveBeenCalledWith('b1', 2026, jasmine.any(File));
    expect((component as unknown as { step: () => string }).step()).toBe('RESULT');
    expect(component.committed).toBeTruthy();
  });

  it('emits closed without committing when dismissed before commit', () => {
    const closedSpy = jasmine.createSpy('closed');
    component.closed.subscribe(closedSpy);

    (component as unknown as { close: () => void }).close();

    expect(targetApi.importCommit).not.toHaveBeenCalled();
    expect(closedSpy).toHaveBeenCalled();
  });
});

// GB-876 FE cases: drive the modal through its DOM like a user (buttons, states, API responses, language).
describe('ImportBranchTargetModalComponent (GB-876 UI)', () => {
  let fixture: ComponentFixture<ImportBranchTargetModalComponent>;
  let targetApi: jasmine.SpyObj<TargetAssignmentService>;
  let i18n: I18nService;

  const row = (overrides: Partial<TargetImportRow>): TargetImportRow => ({
    rowNumber: 2,
    kpiCode: 'HDV',
    kpiName: 'Huy động vốn',
    unit: 'tỷ VND',
    value: 3500,
    previousYearValue: 3000,
    delta: 500,
    deltaPercent: 0.1667,
    action: 'CREATE',
    errors: [],
    warnings: [],
    ...overrides,
  });

  const preview = (overrides: Partial<TargetImportPreview> = {}): TargetImportPreview => ({
    fileName: 'plan.xlsx',
    branchId: 'b1',
    year: 2026,
    summary: { total: 3, valid: 2, invalid: 1, create: 1, update: 1, unchanged: 0 },
    quarterRatios: { q1: null, q2: null, q3: null, q4: null },
    kpisNotInFile: [{ kpiCode: 'DU_NO', kpiName: 'Giải ngân tín dụng' }],
    rows: [
      row({}),
      row({
        rowNumber: 3,
        kpiCode: 'TL_NPL',
        kpiName: 'Tỷ lệ nợ xấu',
        unit: '%',
        value: 0.015,
        previousYearValue: null,
        deltaPercent: null,
        action: 'UPDATE',
      }),
      row({
        rowNumber: 4,
        kpiCode: 'XXX',
        kpiName: null,
        action: 'INVALID',
        errors: [{ column: 'B', code: 'TARGET_IMPORT_KPI_NOT_FOUND', message: 'not found' }],
      }),
    ],
    ...overrides,
  });

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => i18n.localization.importBranchTargetModal;
  const footerButtons = () =>
    Array.from(el().querySelectorAll<HTMLButtonElement>('.modal-footer button'));
  const buttonByText = (label: string) =>
    Array.from(el().querySelectorAll<HTMLButtonElement>('button')).find(
      (b) => b.textContent?.trim() === label,
    );
  const alertText = () => el().querySelector('[role=alert]')?.textContent?.trim();
  const commitLabel = (count: number) => text().commit.replace('{count}', `${count}`);

  function choose(name: string, bytes = 10): void {
    const input = el().querySelector<HTMLInputElement>('#import-file')!;
    Object.defineProperty(input, 'files', {
      value: [new File([new Uint8Array(bytes)], name)],
      configurable: true,
    });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function toPreview(data = preview()): void {
    targetApi.importPreview.and.returnValue(of(data));
    choose('plan.xlsx');
    buttonByText(text().next)!.click();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    targetApi = jasmine.createSpyObj<TargetAssignmentService>('TargetAssignmentService', [
      'importPreview',
      'importCommit',
      'downloadTemplate',
    ]);
    await TestBed.configureTestingModule({
      imports: [ImportBranchTargetModalComponent],
      providers: [{ provide: TargetAssignmentService, useValue: targetApi }],
    }).compileComponents();
    i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');
    fixture = TestBed.createComponent(ImportBranchTargetModalComponent);
    fixture.componentInstance.branchId = 'b1';
    fixture.componentInstance.year = 2026;
    fixture.detectChanges();
  });

  afterEach(() => i18n.setLanguage('vi'));

  it('GB876_07 opens on the choose-file step with Preview disabled', () => {
    const steps = Array.from(el().querySelectorAll('.import-steps li'));
    const yearInput = el().querySelector<HTMLInputElement>('#import-year')!;

    expect(el().querySelector('h2')?.textContent?.trim()).toBe(text().title);
    expect(steps.map((s) => s.classList.contains('is-active'))).toEqual([true, false, false]);
    expect(yearInput.value).toBe('2026');
    expect(yearInput.disabled).toBeTrue();
    expect(el().querySelector('.file-drop')?.textContent).toContain(text().noFileChosen);
    expect(buttonByText(text().next)!.disabled).toBeTrue();
    expect(targetApi.importPreview).not.toHaveBeenCalled();
  });

  it('GB876_08 validates the chosen file and enables Preview only for a valid .xlsx', () => {
    choose('plan.pdf');
    expect(alertText()).toBe(text().errorInvalidExtension);
    expect(buttonByText(text().next)!.disabled).toBeTrue();

    choose('big.xlsx', 10 * 1024 * 1024 + 1);
    expect(alertText()).toBe(text().errorFileTooLarge);
    expect(buttonByText(text().next)!.disabled).toBeTrue();

    choose('plan.xlsx', 2048);
    expect(alertText()).toBeUndefined();
    expect(el().querySelector('.file-name')?.textContent?.trim()).toBe('plan.xlsx');
    expect(buttonByText(text().next)!.disabled).toBeFalse();

    buttonByText(text().removeFile)!.click();
    fixture.detectChanges();
    expect(el().querySelector('.file-name')).toBeNull();
    expect(buttonByText(text().next)!.disabled).toBeTrue();
  });

  it('GB876_09 downloads the template for the selected year', () => {
    targetApi.downloadTemplate.and.returnValue(of(new Blob(['x'])));
    const click = spyOn(HTMLAnchorElement.prototype, 'click');
    const created = spyOn(document, 'createElement').and.callThrough();

    buttonByText(text().downloadTemplate)!.click();

    expect(targetApi.downloadTemplate).toHaveBeenCalledOnceWith(2026);
    const anchor = created.calls
      .all()
      .map((c) => c.returnValue)
      .find((n) => n instanceof HTMLAnchorElement) as HTMLAnchorElement;
    expect(anchor.download).toBe('KPI-Nam-Quy-2026-mau.xlsx');
    expect(click).toHaveBeenCalled();
  });

  it('GB876_10 renders the preview table, summary, badges and checks from the response', () => {
    toPreview();

    expect(targetApi.importPreview).toHaveBeenCalledOnceWith('b1', 2026, jasmine.any(File));
    const steps = Array.from(el().querySelectorAll('.import-steps li'));
    expect(steps.map((s) => s.classList.contains('is-done'))).toEqual([true, false, false]);
    expect(steps.map((s) => s.classList.contains('is-active'))).toEqual([false, true, false]);
    const summary = el().querySelector('.import-summary')!.textContent!.replace(/\s+/g, ' ');
    expect(summary).toContain(`${text().summaryTotal}: 3`);
    expect(summary).toContain(`${text().summaryValid}: 2`);
    expect(summary).toContain(`${text().summaryInvalid}: 1`);
    expect(el().querySelector('[role=status]')?.textContent?.trim()).toBe(text().replaceWarning);

    const rows = Array.from(el().querySelectorAll('.import-preview-table tbody tr'));
    const cells = (r: Element) =>
      Array.from(r.querySelectorAll('td')).map((c) => c.textContent!.trim());
    expect(rows.length).toBe(3);
    expect(cells(rows[0]).slice(0, 4)).toEqual(['2', 'HDV', 'Huy động vốn', 'tỷ VND']);
    expect(cells(rows[0])[7]).toBe(text().actionCreate);
    expect(rows[0].querySelector('.check-ok')).not.toBeNull();
    expect(cells(rows[1])[4]).toBe('—');
    expect(cells(rows[1])[5]).toContain('%');
    expect(cells(rows[1])[6]).toBe('—');
    expect(cells(rows[1])[7]).toBe(text().actionUpdate);
    expect(rows[2].classList).toContain('row-invalid');
    expect(cells(rows[2])[7]).toBe(text().actionInvalid);
    expect(rows[2].querySelector('.check-error')?.textContent?.trim()).toBe(
      text().errors.TARGET_IMPORT_KPI_NOT_FOUND,
    );
    expect(el().querySelector('.kpis-not-in-file summary')?.textContent).toContain('(1)');
    expect(buttonByText(commitLabel(2))!.disabled).toBeFalse();
  });

  it('GB876_11 shows the loading labels and blocks double submit while waiting', () => {
    const pending = new Subject<TargetImportPreview>();
    targetApi.importPreview.and.returnValue(pending);
    choose('plan.xlsx');
    buttonByText(text().next)!.click();
    fixture.detectChanges();

    expect(buttonByText(text().uploading)!.disabled).toBeTrue();
    pending.next(preview());
    fixture.detectChanges();

    const commitPending = new Subject<TargetImportCommitResult>();
    targetApi.importCommit.and.returnValue(commitPending);
    buttonByText(commitLabel(2))!.click();
    fixture.detectChanges();
    const busy = buttonByText(text().committing)!;
    expect(busy.disabled).toBeTrue();
    busy.click();
    expect(targetApi.importCommit).toHaveBeenCalledTimes(1);
  });

  const failures: { name: string; status: number; code?: string; expected: () => string }[] = [
    {
      name: 'a known errorCode',
      status: 400,
      code: 'TARGET_IMPORT_YEAR_COLUMN_MISSING',
      expected: () => text().errors.TARGET_IMPORT_YEAR_COLUMN_MISSING,
    },
    {
      name: 'VALIDATION_ERROR',
      status: 400,
      code: 'VALIDATION_ERROR',
      expected: () => text().errors.VALIDATION_ERROR,
    },
    {
      name: '403 FORBIDDEN (no dedicated text)',
      status: 403,
      code: 'FORBIDDEN',
      expected: () => text().loadFailed,
    },
    { name: '500 with no body', status: 500, expected: () => text().loadFailed },
  ];
  for (const f of failures) {
    it(`GB876_12 preview failure (${f.name}) shows an error and stays on step 1`, () => {
      targetApi.importPreview.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: f.status,
              error: f.code ? { success: false, errorCode: f.code } : null,
            }),
        ),
      );
      choose('plan.xlsx');
      buttonByText(text().next)!.click();
      fixture.detectChanges();

      expect(alertText()).toBe(f.expected());
      expect(el().querySelector('#import-file')).not.toBeNull();
      expect(buttonByText(text().next)!.disabled).toBeFalse();
    });
  }

  it('GB876_12 commit failure shows the commit error and keeps the preview', () => {
    toPreview();
    targetApi.importCommit.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    buttonByText(commitLabel(2))!.click();
    fixture.detectChanges();

    expect(alertText()).toBe(text().commitFailed);
    expect(el().querySelectorAll('.import-preview-table tbody tr').length).toBe(3);
  });

  it('GB876_13 disables commit when no row is valid and hides the warnings', () => {
    toPreview(
      preview({
        summary: { total: 1, valid: 0, invalid: 1, create: 0, update: 0, unchanged: 0 },
        rows: [
          row({
            action: 'INVALID',
            errors: [{ column: 'E', code: 'TARGET_IMPORT_INVALID_VALUE', message: '' }],
          }),
        ],
        kpisNotInFile: [],
      }),
    );

    expect(buttonByText(commitLabel(0))!.disabled).toBeTrue();
    expect(el().querySelector('[role=status]')).toBeNull();
    expect(el().querySelector('.kpis-not-in-file')).toBeNull();
  });

  it('GB876_14 Back keeps the file, commit shows the result and Done emits committed then closed', () => {
    toPreview();
    buttonByText(text().back)!.click();
    fixture.detectChanges();
    expect(el().querySelector('.file-name')?.textContent?.trim()).toBe('plan.xlsx');

    buttonByText(text().next)!.click();
    fixture.detectChanges();
    targetApi.importCommit.and.returnValue(
      of({ branchId: 'b1', year: 2026, targets: [], committed: 2, skipped: 1, needsReview: true }),
    );
    buttonByText(commitLabel(2))!.click();
    fixture.detectChanges();

    const notices = Array.from(el().querySelectorAll('[role=status]')).map((n) =>
      n.textContent!.trim(),
    );
    expect(notices).toEqual([
      text()
        .resultSummary.replace('{committed}', '2')
        .replace('{total}', '3')
        .replace('{skipped}', '1'),
      text().needsReviewWarning,
    ]);
    const events: string[] = [];
    fixture.componentInstance.committed.subscribe(() => events.push('committed'));
    fixture.componentInstance.closed.subscribe(() => events.push('closed'));
    expect(footerButtons().map((b) => b.textContent!.trim())).toEqual([text().done]);
    buttonByText(text().done)!.click();
    expect(events).toEqual(['committed', 'closed']);
  });

  it('GB876_14 Cancel, × and the backdrop close without committing', () => {
    const closed = jasmine.createSpy('closed');
    const committed = jasmine.createSpy('committed');
    fixture.componentInstance.closed.subscribe(closed);
    fixture.componentInstance.committed.subscribe(committed);

    buttonByText(text().cancel)!.click();
    el().querySelector<HTMLButtonElement>('.modal-close')!.click();
    el().querySelector<HTMLElement>('.modal-backdrop')!.click();
    el().querySelector<HTMLElement>('.modal-dialog')!.click();

    expect(closed).toHaveBeenCalledTimes(3);
    expect(committed).not.toHaveBeenCalled();
    expect(el().querySelector('.modal-close')!.getAttribute('aria-label')).toBe(text().close);
  });

  it('GB876_15 switches every visible label to English at runtime', () => {
    const viTitle = text().title;
    i18n.setLanguage('en');
    fixture.detectChanges();
    const en = text();

    expect(en.title).not.toBe(viTitle);
    expect(el().querySelector('h2')?.textContent?.trim()).toBe(en.title);
    expect(buttonByText(en.next)).toBeDefined();
    expect(buttonByText(en.cancel)).toBeDefined();
    expect(buttonByText(en.downloadTemplate)).toBeDefined();
    expect(el().querySelector('.modal-close')!.getAttribute('aria-label')).toBe(en.close);
    choose('plan.pdf');
    expect(alertText()).toBe(en.errorInvalidExtension);
  });
});
