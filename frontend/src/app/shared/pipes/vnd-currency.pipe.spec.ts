import { TestBed } from '@angular/core/testing';
import { VndCurrencyPipe } from './vnd-currency.pipe';
import { I18nService } from '../../core/i18n/i18n.service';

describe('VndCurrencyPipe', () => {
  let pipe: VndCurrencyPipe;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [I18nService, VndCurrencyPipe],
    });
    pipe = TestBed.inject(VndCurrencyPipe);
  });

  it('should format numbers with VND suffix and dot thousand separators', () => {
    expect(pipe.transform(350000000)).toBe('350.000.000 VND');
    expect(pipe.transform(1500000)).toBe('1.500.000 VND');
    expect(pipe.transform(0)).toBe('0 VND');
  });

  it('should support custom currency symbol like ₫', () => {
    expect(pipe.transform(45000000, '₫')).toBe('45.000.000 ₫');
  });

  it('should handle numeric string inputs', () => {
    expect(pipe.transform('18000000')).toBe('18.000.000 VND');
  });

  it('should handle negative values', () => {
    expect(pipe.transform(-100000)).toBe('-100.000 VND');
  });

  it('should return 0 VND for null or undefined', () => {
    expect(pipe.transform(null)).toBe('0 VND');
    expect(pipe.transform(undefined)).toBe('0 VND');
  });
});
