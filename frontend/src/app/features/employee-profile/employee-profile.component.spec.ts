import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EmployeeProfileComponent } from './employee-profile.component';
import { I18nService } from '../../core/i18n/i18n.service';

describe('EmployeeProfileComponent', () => {
  let component: EmployeeProfileComponent;
  let fixture: ComponentFixture<EmployeeProfileComponent>;
  let i18nService: I18nService;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [EmployeeProfileComponent],
      providers: [I18nService],
    }).compileComponents();

    fixture = TestBed.createComponent(EmployeeProfileComponent);
    component = fixture.componentInstance;
    i18nService = TestBed.inject(I18nService);
    fixture.detectChanges();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('should create the component with default Vietnamese language', () => {
    expect(component).toBeTruthy();
    expect(component.currentLanguage()).toBe('vi');
    expect(i18nService.currentLanguage()).toBe('vi');
  });

  it('should toggle language between vi and en when toggleLanguage is called', () => {
    expect(component.currentLanguage()).toBe('vi');

    component.toggleLanguage();
    fixture.detectChanges();
    expect(component.currentLanguage()).toBe('en');

    component.toggleLanguage();
    fixture.detectChanges();
    expect(component.currentLanguage()).toBe('vi');
  });

  it('should render language toggle button with active locale class', () => {
    const toggleBtn = fixture.nativeElement.querySelector('#language-toggle-btn');
    expect(toggleBtn).toBeTruthy();

    const viSpan = toggleBtn.querySelector('span:first-child');
    expect(viSpan.classList.contains('active-locale')).toBeTrue();

    component.toggleLanguage();
    fixture.detectChanges();

    const enSpan = toggleBtn.querySelector('span:last-child');
    expect(enSpan.classList.contains('active-locale')).toBeTrue();
  });

  it('should render date formatted as DD/MM/YYYY', () => {
    const textContent = fixture.nativeElement.textContent;
    // Verify joinDate or dueDate formatted with DD/MM/YYYY
    expect(textContent).toContain('15/03/2023');
    expect(textContent).toContain('28/10/2026');
  });

  it('should render currency formatted in VND', () => {
    const textContent = fixture.nativeElement.textContent;
    expect(textContent).toContain('350.000.000 VND');
    expect(textContent).toContain('315.200.000 VND');
  });

  it('should update translated text when toggling language to English', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.product-title strong')?.textContent?.trim()).toBe(
      'Hệ thống Quản trị & Điều hành Ngân hàng',
    );

    component.toggleLanguage();
    fixture.detectChanges();

    expect(compiled.querySelector('.product-title strong')?.textContent?.trim()).toBe(
      'Banking Performance Management System',
    );
  });
});
