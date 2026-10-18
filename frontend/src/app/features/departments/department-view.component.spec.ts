import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { DepartmentService } from '../../core/department.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { DepartmentViewComponent } from './department-view.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

describe('DepartmentViewComponent', () => {
  const api = { list: jasmine.createSpy('list') };
  beforeEach(async () => {
    api.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 20 }));
    await TestBed.configureTestingModule({
      imports: [DepartmentViewComponent],
      providers: [provideRouter([]), { provide: DepartmentService, useValue: api }],
    })
      .overrideComponent(DepartmentViewComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
  });
  it('renders a real empty response and updates text and accessibility labels live', () => {
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');
    const fixture = TestBed.createComponent(DepartmentViewComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.textContent).toContain('Chưa có phòng ban');
    expect(root.querySelector('select')?.getAttribute('aria-label')).toBe('Chọn phòng ban');
    i18n.setLanguage('en');
    fixture.detectChanges();
    expect(root.textContent).toContain('No departments are available');
    expect(root.querySelector('select')?.getAttribute('aria-label')).toBe('Select department');
    expect(root.textContent).not.toContain('Retail Banking');
  });
  it('shows an error and allows a retry without injecting mock records', () => {
    api.list.and.returnValue(throwError(() => new Error('403')));
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('en');
    const fixture = TestBed.createComponent(DepartmentViewComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Unable to load');
    i18n.setLanguage('vi');
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Không thể tải');
    api.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 20 }));
    root.querySelector('button')?.click();
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(root.textContent).toContain('Chưa có phòng ban');
  });
});
