import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { WorkTypeDetail } from '../../core/work-type.models';
import { WorkTypeService } from '../../core/work-type.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { WorkTypeDetailComponent } from './work-type-detail.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const workType: WorkTypeDetail = {
  id: '5eed0000-0000-0000-000c-000000000002',
  code: 'HUY_DONG',
  name: 'Huy động tiền gửi',
  description: null,
  status: 'ACTIVE',
  createdAt: '2026-01-01T00:00:00+00:00',
  kpis: [
    {
      kpiDefinitionId: 'k1',
      kpiCode: 'HDV',
      kpiName: 'Huy động vốn',
      isRequired: true,
      effectiveFrom: '2026-01-01',
      effectiveTo: null,
    },
    {
      kpiDefinitionId: 'k2',
      kpiCode: 'OLD',
      kpiName: 'Chỉ tiêu cũ',
      isRequired: false,
      effectiveFrom: '2020-01-01',
      effectiveTo: '2020-12-31',
    },
  ],
};

describe('WorkTypeDetailComponent', () => {
  const api = { list: jasmine.createSpy('list') };
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  beforeEach(async () => {
    api.list.calls.reset();
    api.list.and.returnValue(of({ items: [workType], totalCount: 1, page: 1, pageSize: 100 }));
    params = new BehaviorSubject(convertToParamMap({}));
    await TestBed.configureTestingModule({
      imports: [WorkTypeDetailComponent],
      providers: [
        provideRouter([]),
        { provide: WorkTypeService, useValue: api },
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: params,
            get snapshot() {
              return { paramMap: params.value };
            },
          },
        },
      ],
    })
      .overrideComponent(WorkTypeDetailComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('renders API data only and reacts to language changes', () => {
    const fixture = TestBed.createComponent(WorkTypeDetailComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(api.list).toHaveBeenCalledWith({ pageSize: 100 });
    expect(root.textContent).toContain('Huy động tiền gửi');
    expect(root.textContent).not.toContain('WT-2026-008');
    expect(root.querySelector('select')?.getAttribute('aria-label')).toBe('Chọn loại công việc');
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(root.querySelector('select')?.getAttribute('aria-label')).toBe('Select work type');
  });

  it('derives mapping state from dates and can hide expired mappings', () => {
    const fixture = TestBed.createComponent(WorkTypeDetailComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelectorAll('tbody tr').length).toBe(2);
    const toggle = root.querySelector<HTMLInputElement>('input[type="checkbox"]');
    toggle?.click();
    fixture.detectChanges();
    expect(root.querySelectorAll('tbody tr').length).toBe(1);
    expect(root.textContent).not.toContain('Chỉ tiêu cũ');
  });

  it('shows a real empty state', () => {
    api.list.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 100 }));
    const fixture = TestBed.createComponent(WorkTypeDetailComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Chưa có loại công việc');
  });

  it('shows an error with retry and no sample record', () => {
    api.list.and.returnValue(throwError(() => new Error('500')));
    const fixture = TestBed.createComponent(WorkTypeDetailComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[role="alert"]')).not.toBeNull();
    expect(root.textContent).not.toContain('WT-2026-008');
    api.list.and.returnValue(of({ items: [workType], totalCount: 1, page: 1, pageSize: 100 }));
    root.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(root.textContent).toContain('Huy động tiền gửi');
  });
});
