import { TestBed } from '@angular/core/testing';
import { I18nService } from './i18n.service';

describe('I18nService', () => {
  let service: I18nService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [I18nService],
    });
    service = TestBed.inject(I18nService);
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('should be created and default to Vietnamese (vi)', () => {
    expect(service).toBeTruthy();
    expect(service.currentLanguage()).toBe('vi');
    expect(service.currentFormatConfig().currency).toBe('VND');
    expect(service.currentFormatConfig().date).toBe('DD/MM/YYYY');
  });

  it('should toggle language between vi and en', () => {
    expect(service.currentLanguage()).toBe('vi');

    const switchedToEn = service.toggleLanguage();
    expect(switchedToEn).toBe('en');
    expect(service.currentLanguage()).toBe('en');

    const switchedBackToVi = service.toggleLanguage();
    expect(switchedBackToVi).toBe('vi');
    expect(service.currentLanguage()).toBe('vi');
  });

  it('should set language explicitly and persist to storage', () => {
    service.setLanguage('en');
    expect(service.currentLanguage()).toBe('en');
    expect(localStorage.getItem('bpms_language')).toBe('en');

    service.setLanguage('vi');
    expect(service.currentLanguage()).toBe('vi');
    expect(localStorage.getItem('bpms_language')).toBe('vi');
  });

  it('should translate keys for vi and en', () => {
    service.setLanguage('vi');
    expect(service.translate('common.appTitle')).toBe('Hệ thống Quản trị & Điều hành Ngân hàng');
    expect(service.translate('common.help')).toBe('Trợ giúp');

    service.setLanguage('en');
    expect(service.translate('common.appTitle')).toBe('Banking Performance Management System');
    expect(service.translate('common.help')).toBe('Help');
  });

  it('should fall back to vi if key is not found in en, and return key if missing completely', () => {
    service.setLanguage('en');
    expect(service.translate('non.existent.key')).toBe('non.existent.key');
    expect(service.translate('')).toBe('');
  });

  it('should interpolate params in translations', () => {
    const text = service.translate('common.test', { name: 'Admin' });
    expect(text).toBe('common.test');
  });

  describe('formatDate (DD/MM/YYYY)', () => {
    it('should format Date instance to DD/MM/YYYY by default', () => {
      const date = new Date(2026, 9, 16); // 16 Oct 2026
      expect(service.formatDate(date)).toBe('16/10/2026');
    });

    it('should format ISO string to DD/MM/YYYY', () => {
      expect(service.formatDate('2026-10-16T08:30:00.000Z')).toMatch(/^\d{2}\/\d{2}\/2026$/);
    });

    it('should support custom pattern like DD/MM/YYYY HH:mm', () => {
      const date = new Date(2026, 0, 5, 9, 7, 3);
      expect(service.formatDate(date, 'DD/MM/YYYY HH:mm:ss')).toBe('05/01/2026 09:07:03');
    });

    it('should handle null, undefined, and empty string gracefully', () => {
      expect(service.formatDate(null)).toBe('-');
      expect(service.formatDate(undefined)).toBe('-');
      expect(service.formatDate('')).toBe('-');
    });
  });

  describe('formatCurrency (VND)', () => {
    it('should format numbers to VND currency with dot thousand separator', () => {
      expect(service.formatCurrency(1000000)).toBe('1.000.000 VND');
      expect(service.formatCurrency(350000000)).toBe('350.000.000 VND');
      expect(service.formatCurrency(0)).toBe('0 VND');
    });

    it('should support custom symbol like ₫ or empty suffix', () => {
      expect(service.formatCurrency(500000, '₫')).toBe('500.000 ₫');
      expect(service.formatCurrency(500000, '')).toBe('500.000');
    });

    it('should handle negative numbers correctly', () => {
      expect(service.formatCurrency(-250000)).toBe('-250.000 VND');
    });

    it('should handle null and undefined', () => {
      expect(service.formatCurrency(null)).toBe('0 VND');
      expect(service.formatCurrency(undefined)).toBe('0 VND');
    });
  });

  describe('formatNumber', () => {
    it('should format numbers with Vietnamese dot thousand separator and comma decimal', () => {
      expect(service.formatNumber(1234567)).toBe('1.234.567');
      expect(service.formatNumber(82.4)).toBe('82,4');
      expect(service.formatNumber(1234.56, 2)).toBe('1.234,56');
    });

    it('should handle negative numbers and null/undefined', () => {
      expect(service.formatNumber(-1234)).toBe('-1.234');
      expect(service.formatNumber(null)).toBe('0');
      expect(service.formatNumber(undefined)).toBe('0');
    });
  });
});
