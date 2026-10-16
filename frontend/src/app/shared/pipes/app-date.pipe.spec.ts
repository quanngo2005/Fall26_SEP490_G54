import { TestBed } from '@angular/core/testing';
import { AppDatePipe } from './app-date.pipe';
import { I18nService } from '../../core/i18n/i18n.service';

describe('AppDatePipe', () => {
  let pipe: AppDatePipe;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [I18nService, AppDatePipe],
    });
    pipe = TestBed.inject(AppDatePipe);
  });

  it('should format Date object to DD/MM/YYYY by default', () => {
    const date = new Date(2026, 9, 28);
    expect(pipe.transform(date)).toBe('28/10/2026');
  });

  it('should format ISO date string to DD/MM/YYYY', () => {
    const formatted = pipe.transform('2026-10-30T12:00:00Z');
    expect(formatted).toMatch(/^\d{2}\/\d{2}\/2026$/);
  });

  it('should support custom pattern parameter', () => {
    const date = new Date(2026, 4, 15, 14, 30);
    expect(pipe.transform(date, 'DD/MM/YYYY HH:mm')).toBe('15/05/2026 14:30');
  });

  it('should return "-" for empty or null dates', () => {
    expect(pipe.transform(null)).toBe('-');
    expect(pipe.transform(undefined)).toBe('-');
    expect(pipe.transform('')).toBe('-');
  });
});
