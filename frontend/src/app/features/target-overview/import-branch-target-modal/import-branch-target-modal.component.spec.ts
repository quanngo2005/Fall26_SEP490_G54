import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
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
