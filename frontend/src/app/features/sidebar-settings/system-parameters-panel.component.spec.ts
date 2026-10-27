import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SystemParameter } from '../../core/system-parameter.models';
import { SystemParameterService } from '../../core/system-parameter.service';
import { SystemParametersPanelComponent } from './system-parameters-panel.component';

describe('SystemParametersPanelComponent', () => {
  const parameters: SystemParameter[] = [
    {
      key: 'appealWindowDays',
      value: 5,
      min: 1,
      max: 30,
      locked: false,
      updatedAt: '2026-10-01T03:00:00Z',
      updatedBy: 'sa.admin',
    },
    {
      key: 'handoverThresholdVnd',
      value: 50000000,
      min: 1,
      max: Number.MAX_SAFE_INTEGER,
      locked: false,
      updatedAt: '2026-10-25T03:00:00Z',
      updatedBy: 'sa.admin',
    },
    {
      key: 'kpiLockTime',
      value: '23:59:59',
      min: null,
      max: null,
      locked: true,
      updatedAt: null,
      updatedBy: null,
    },
  ];
  let list: jasmine.Spy;
  let save: jasmine.Spy;

  function create() {
    const fixture = TestBed.createComponent(SystemParametersPanelComponent);
    fixture.detectChanges();
    return fixture;
  }
  function type(fixture: ReturnType<typeof create>, id: string, value: string) {
    const input: HTMLInputElement = fixture.nativeElement.querySelector(`#${id}`);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    return input;
  }

  beforeEach(() => {
    localStorage.clear();
    list = jasmine.createSpy('list').and.returnValue(of(parameters));
    save = jasmine.createSpy('save').and.callFake(() => of(parameters));
    TestBed.configureTestingModule({
      providers: [{ provide: SystemParameterService, useValue: { list, save } }],
    });
  });
  afterEach(() => localStorage.clear());

  it('marks edited fields, validates ranges and blocks saving invalid values', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    expect(component.dirty()).toBeFalse();
    type(fixture, 'param-appealWindowDays', '31');
    expect(component.dirty()).toBeTrue();
    expect(component.canSave()).toBeFalse();
    expect(fixture.nativeElement.querySelector('#error-appealWindowDays')).not.toBeNull();
    component.requestSave();
    expect(component.confirming()).toBeFalse();
    type(fixture, 'param-appealWindowDays', '7');
    expect(component.canSave()).toBeTrue();
    expect(fixture.nativeElement.querySelector('.dot')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.banner').textContent).toContain('1 thay đổi');
    fixture.destroy();
  });

  it('formats the VND threshold, keeps locked fields read-only and discards drafts', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    const locked: HTMLInputElement = fixture.nativeElement.querySelector('#param-kpiLockTime');
    expect(locked.readOnly).toBeTrue();
    const input = type(fixture, 'param-handoverThresholdVnd', '80000000');
    expect(input.value).toMatch(/80[.,]000[.,]000/);
    expect(component.diffs()).toEqual([
      { key: 'handoverThresholdVnd', oldValue: 50000000, newValue: 80000000 },
    ]);
    component.discard();
    fixture.detectChanges();
    expect(component.dirty()).toBeFalse();
    fixture.destroy();
  });

  it('saves the changed keys only after confirmation and shows old and new values', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    type(fixture, 'param-appealWindowDays', '7');
    component.requestSave();
    fixture.detectChanges();
    const dialog: HTMLElement = fixture.nativeElement.querySelector('[role="dialog"]');
    expect(dialog.textContent).toContain('Xác nhận lưu tham số');
    expect(dialog.textContent).toContain('5 ngày');
    expect(dialog.textContent).toContain('7 ngày');
    expect(save).not.toHaveBeenCalled();
    component.confirmSave();
    expect(save).toHaveBeenCalledWith({ changes: [{ key: 'appealWindowDays', value: 7 }] });
    expect(component.saved()).toBeTrue();
    expect(component.dirty()).toBeFalse();
    fixture.destroy();
  });

  it('keeps the draft and reports an error when saving or loading fails', () => {
    save.and.returnValue(throwError(() => new Error('boom')));
    const fixture = create();
    const component = fixture.componentInstance;
    type(fixture, 'param-appealWindowDays', '7');
    component.requestSave();
    component.confirmSave();
    fixture.detectChanges();
    expect(component.error()).toBe('saveFailed');
    expect(component.dirty()).toBeTrue();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Không thể lưu',
    );
    list.and.returnValue(throwError(() => new Error('down')));
    component.load();
    fixture.detectChanges();
    expect(component.error()).toBe('loadFailed');
    fixture.destroy();
  });
});
