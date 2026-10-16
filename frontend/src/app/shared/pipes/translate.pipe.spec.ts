import { TestBed } from '@angular/core/testing';
import { TranslatePipe } from './translate.pipe';
import { I18nService } from '../../core/i18n/i18n.service';

describe('TranslatePipe', () => {
  let pipe: TranslatePipe;
  let service: I18nService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [I18nService, TranslatePipe],
    });
    service = TestBed.inject(I18nService);
    pipe = TestBed.inject(TranslatePipe);
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('should translate key using I18nService in default vi language', () => {
    expect(pipe.transform('common.help')).toBe('Trợ giúp');
    expect(pipe.transform('common.appTitle')).toBe('Hệ thống Quản trị & Điều hành Ngân hàng');
  });

  it('should reflect language changes dynamically when toggled', () => {
    expect(pipe.transform('common.help')).toBe('Trợ giúp');
    service.setLanguage('en');
    expect(pipe.transform('common.help')).toBe('Help');
  });

  it('should return raw key if key is missing', () => {
    expect(pipe.transform('unknown.key')).toBe('unknown.key');
  });
});
