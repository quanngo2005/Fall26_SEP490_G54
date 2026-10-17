import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SidebarConfigurationService } from '../../core/sidebar-configuration.service';
import { EmployeeProfileComponent } from './employee-profile.component';

describe('EmployeeProfileComponent', () => {
  let component: EmployeeProfileComponent;
  let fixture: ComponentFixture<EmployeeProfileComponent>;
  let i18nService: I18nService;
  const user = signal({
    id: 'emp-001',
    email: 'user@example.test',
    fullName: 'Nguyễn Văn A',
    roles: ['EMPLOYEE'],
    permissions: ['WORK_READ'],
  });

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [EmployeeProfileComponent],
      providers: [
        I18nService,
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: { currentUser: user.asReadonly(), logout: () => of(null) },
        },
        {
          provide: SidebarConfigurationService,
          useValue: { current: () => of([]) },
        },
      ],
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

  it('should render language toggle button with active locale in HeaderComponent', () => {
    const buttons = fixture.nativeElement.querySelectorAll('.language-switch button');
    expect(buttons.length).toBe(2);

    const viBtn = buttons[0];
    const enBtn = buttons[1];
    expect(viBtn.classList.contains('selected')).toBeTrue();

    enBtn.click();
    fixture.detectChanges();

    expect(enBtn.classList.contains('selected')).toBeTrue();
    expect(i18nService.currentLanguage()).toBe('en');
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
    expect(compiled.textContent).toContain('Quay lại danh sách cán bộ');

    component.toggleLanguage();
    fixture.detectChanges();

    expect(compiled.textContent).toContain('Back to Employee List');
  });
});

