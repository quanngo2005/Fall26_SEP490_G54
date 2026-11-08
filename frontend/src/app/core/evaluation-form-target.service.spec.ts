import { TestBed } from '@angular/core/testing';
import { EvaluationFormTargetService } from './evaluation-form-target.service';

describe('EvaluationFormTargetService', () => {
  let service: EvaluationFormTargetService;
  const target = { employeeId: 'e1', periodId: 'p1' };

  beforeEach(() => {
    sessionStorage.clear();
    service = TestBed.inject(EvaluationFormTargetService);
  });

  afterEach(() => sessionStorage.clear());

  it('reads back the target it saved', () => {
    service.save(target);
    expect(service.read()).toEqual(target);
  });

  it('keeps the latest target when saved twice', () => {
    service.save(target);
    service.save({ employeeId: 'e2', periodId: 'p1' });
    expect(service.read()).toEqual({ employeeId: 'e2', periodId: 'p1' });
  });

  it('reads null when nothing was saved', () => {
    expect(service.read()).toBeNull();
  });

  it('reads null when the stored value is not JSON', () => {
    service.save(target);
    sessionStorage.setItem('g54.evaluation.formTarget', '{broken');
    expect(service.read()).toBeNull();
  });

  it('reads null when the stored value is not a target', () => {
    for (const stored of [
      'null',
      '"text"',
      '{"employeeId":"e1"}',
      '{"employeeId":"","periodId":"p1"}',
      '{"employeeId":1,"periodId":"p1"}',
    ]) {
      sessionStorage.setItem('g54.evaluation.formTarget', stored);
      expect(service.read()).withContext(stored).toBeNull();
    }
  });

  it('drops extra fields from what it reads', () => {
    sessionStorage.setItem(
      'g54.evaluation.formTarget',
      '{"employeeId":"e1","periodId":"p1","other":"x"}',
    );
    expect(service.read()).toEqual(target);
  });

  it('does not throw when storage refuses to write', () => {
    spyOn(Storage.prototype, 'setItem').and.throwError('quota');
    expect(() => service.save(target)).not.toThrow();
  });
});
