import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { DepartmentService } from '../../core/department.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { DepartmentViewComponent } from './department-view.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

describe('DepartmentViewComponent', () => {
  const api = {
    list: jasmine.createSpy('list'),
    overview: jasmine.createSpy('overview'),
    members: jasmine.createSpy('members'),
  };
  let query: Record<string, string>;
  beforeEach(async () => {
    query = {};
    api.overview.calls.reset();
    api.members.calls.reset();
    api.overview.and.returnValue(new Subject());
    api.members.and.returnValue(new Subject());
    api.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 20 }));
    await TestBed.configureTestingModule({
      imports: [DepartmentViewComponent],
      providers: [
        provideRouter([]),
        { provide: DepartmentService, useValue: api },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              get queryParamMap() {
                return convertToParamMap(query);
              },
            },
          },
        },
      ],
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

  describe('choosing the department from the link', () => {
    const items = [
      { id: 'id-branch', code: 'CN-HN', name: 'Chi nhánh', levelType: 'BRANCH' },
      { id: 'id-sales', code: 'CN-HN-KD', name: 'Phòng Kinh doanh', levelType: 'DEPARTMENT' },
      { id: 'id-teller', code: 'CN-HN-GD', name: 'Phòng Giao dịch', levelType: 'DEPARTMENT' },
    ];
    const open = () => {
      api.list.and.returnValue(of({ items, totalCount: 3, page: 1, pageSize: 100 }));
      const fixture = TestBed.createComponent(DepartmentViewComponent);
      fixture.detectChanges();
    };

    it('selects the department named by ?department=<code>', () => {
      query = { department: 'CN-HN-GD' };

      open();

      expect(api.overview).toHaveBeenCalledWith('id-teller', undefined);
    });

    it('still honours a link from before URLs used codes (?departmentId=<guid>)', () => {
      query = { departmentId: 'id-teller' };

      open();

      expect(api.overview).toHaveBeenCalledWith('id-teller', undefined);
    });

    it('prefers the code when both are present', () => {
      query = { department: 'CN-HN-KD', departmentId: 'id-teller' };

      open();

      expect(api.overview).toHaveBeenCalledWith('id-sales', undefined);
    });

    it('falls back to the first department when the code is unknown', () => {
      query = { department: 'NOPE' };

      open();

      expect(api.overview).toHaveBeenCalledWith('id-sales', undefined);
    });
  });
});
