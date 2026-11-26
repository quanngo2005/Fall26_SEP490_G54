import { TestBed } from '@angular/core/testing';
import { I18nService } from '../../../core/i18n/i18n.service';
import { TargetMoneyPipe } from './target-money.pipe';

describe('TargetMoneyPipe', () => {
  let pipe: TargetMoneyPipe;

  beforeEach(() => {
    TestBed.inject(I18nService).setLanguage('vi');
    pipe = TestBed.runInInjectionContext(() => new TargetMoneyPipe());
  });

  it('scales to tỷ at 1 billion and up', () => {
    expect(pipe.transform(2_500_000_000)).toBe('2,5 tỷ');
  });

  it('scales to tr between 1 million and 1 billion', () => {
    expect(pipe.transform(45_000_000)).toBe('45 tr');
  });

  it('shows a plain grouped number under 1 million', () => {
    expect(pipe.transform(12_345)).toBe('12.345');
  });

  it('keeps the sign on negative values', () => {
    expect(pipe.transform(-10_000_000)).toBe('-10 tr');
  });

  it('falls back to 0 for null/undefined/NaN', () => {
    expect(pipe.transform(null)).toBe('0');
    expect(pipe.transform(undefined)).toBe('0');
    expect(pipe.transform(NaN)).toBe('0');
  });

  it('accepts an explicit label override', () => {
    expect(pipe.transform(3_000_000_000, { million: 'm', billion: 'bn' })).toBe('3 bn');
  });
});
