import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SidebarConfigurationService } from '../../core/sidebar-configuration.service';
import { WorkTypeDetailComponent } from './work-type-detail.component';

describe('WorkTypeDetailComponent', () => {
  let component: WorkTypeDetailComponent;
  let fixture: ComponentFixture<WorkTypeDetailComponent>;
  const user = signal({
    id: 'emp-002',
    email: 'admin@example.test',
    fullName: 'Nguyễn Văn An',
    roles: ['ADMIN'],
    permissions: ['WORK_READ', 'WORK_MANAGE'],
  });

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [WorkTypeDetailComponent],
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

    fixture = TestBed.createComponent(WorkTypeDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('should create the component successfully with header and sidebar', () => {
    expect(component).toBeTruthy();
    const sidebar = fixture.nativeElement.querySelector('app-sidebar');
    const header = fixture.nativeElement.querySelector('app-header');
    expect(sidebar).toBeTruthy();
    expect(header).toBeTruthy();
  });

  it('should render page content and title', () => {
    const textContent = fixture.nativeElement.textContent;
    expect(textContent).toContain('WT-2026-008');
  });
});
