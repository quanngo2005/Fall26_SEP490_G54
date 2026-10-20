import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { KpiCreateModalComponent } from './kpi-create-modal/kpi-create-modal.component';
import { KpiDefinitionsComponent } from './kpi-definitions.component';

@Component({ selector: 'app-header', template: '' })
class HeaderStub {}
@Component({ selector: 'app-sidebar', template: '' })
class SidebarStub {}

const kpi: KpiDefinition = {
  id: '5eed0000-0000-0000-0005-000000000001',
  kpiGroupId: '5eed0000-0000-0000-0004-000000000001',
  kpiGroupName: 'Kinh doanh',
  code: 'HDV',
  name: 'Huy động vốn',
  description: null,
  formula: null,
  unit: 'tỷ VND',
  aggregationType: 'SUM',
  direction: 'HIGHER_BETTER',
  maxAchievementPercent: 150,
  status: 'ACTIVE',
  updatedAt: '2026-10-14T08:30:00+00:00',
};
const groups = [{ id: kpi.kpiGroupId, code: 'BUS', name: 'Kinh doanh' }];
const page = (items: KpiDefinition[]) => ({
  items,
  totalCount: items.length,
  page: 1,
  pageSize: 20,
});

describe('KpiDefinitionsComponent', () => {
  const api = {
    list: jasmine.createSpy('list'),
    groups: jasmine.createSpy('groups'),
    update: jasmine.createSpy('update'),
    create: jasmine.createSpy('create'),
  };

  const create = async () => {
    const fixture = TestBed.createComponent(KpiDefinitionsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  };

  const edit = async (fixture: Awaited<ReturnType<typeof create>>, name: string) => {
    const root: HTMLElement = fixture.nativeElement;
    const input = root.querySelector<HTMLInputElement>('#kpi-name')!;
    input.value = name;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
    root.querySelector<HTMLFormElement>('form.form-grid')?.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  };

  beforeEach(async () => {
    Object.values(api).forEach((spy) => spy.calls.reset());
    api.list.and.returnValue(of(page([kpi])));
    api.groups.and.returnValue(of(groups));
    await TestBed.configureTestingModule({
      imports: [KpiDefinitionsComponent],
      providers: [{ provide: KpiDefinitionService, useValue: api }],
    })
      .overrideComponent(KpiDefinitionsComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent] },
        add: { imports: [HeaderStub, SidebarStub] },
      })
      .compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('loads the catalog from the API and selects the first KPI', async () => {
    const fixture = await create();
    const root: HTMLElement = fixture.nativeElement;
    expect(api.list).toHaveBeenCalledWith({ keyword: '', kpiGroupId: '', page: 1, pageSize: 20 });
    expect(root.textContent).toContain('Huy động vốn');
    expect(root.querySelector<HTMLInputElement>('#kpi-name')?.value).toBe('Huy động vốn');
    expect(root.querySelector<HTMLInputElement>('#kpi-code')?.disabled).toBeTrue();
  });

  it('shows a real empty state without sample KPIs and updates labels live', async () => {
    api.list.and.returnValue(of(page([])));
    const fixture = await create();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.textContent).toContain('Không tìm thấy KPI phù hợp');
    expect(root.textContent).not.toContain('KPI-RET-01');
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(root.textContent).toContain('No matching KPI');
    expect(root.querySelector('input[type="search"]')?.getAttribute('aria-label')).toBe(
      'Search KPI code/name...',
    );
  });

  it('shows a load error with retry and keeps the failure visible', async () => {
    api.list.and.returnValue(throwError(() => new Error('401')));
    const fixture = await create();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Không thể tải');
    api.list.and.returnValue(of(page([kpi])));
    root.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    fixture.detectChanges();
    expect(root.textContent).toContain('Huy động vốn');
  });

  it('sends the wire payload on save and reports success only after the API answers', async () => {
    api.update.and.returnValue(of({ ...kpi, name: 'Tên mới' }));
    const fixture = await create();
    await edit(fixture, 'Tên mới');
    expect(api.update).toHaveBeenCalledWith(kpi.id, {
      kpiGroupId: kpi.kpiGroupId,
      code: 'HDV',
      name: 'Tên mới',
      description: null,
      formula: null,
      unit: 'tỷ VND',
      aggregationType: 'SUM',
      direction: 'HIGHER_BETTER',
      maxAchievementPercent: 150,
    });
    expect(fixture.nativeElement.querySelector('.notice')?.textContent).toContain('Đã lưu');
  });

  it('keeps a failed save as an error', async () => {
    api.update.and.returnValue(throwError(() => new Error('500')));
    const fixture = await create();
    await edit(fixture, 'Tên mới');
    expect(fixture.nativeElement.querySelector('.notice.is-error')?.textContent).toContain(
      'thất bại',
    );
  });

  it('labels role weights and multipliers as unsaved drafts and never sends them', async () => {
    const fixture = await create();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.draft-panel .status-badge')?.textContent).toContain('Chưa lưu');
    root.querySelectorAll<HTMLButtonElement>('.draft-panel > button')[0].click();
    fixture.detectChanges();
    expect(root.querySelectorAll('.draft-row').length).toBe(1);
    expect(api.update).not.toHaveBeenCalled();
  });

  it('creates a KPI through the modal and rejects an empty code locally', async () => {
    api.create.and.returnValue(of({ ...kpi, id: 'new', code: 'NEW-1' }));
    const fixture = await create();
    fixture.componentInstance['openCreate']();
    fixture.detectChanges();
    const modal = fixture.debugElement.query(By.directive(KpiCreateModalComponent))
      .componentInstance as KpiCreateModalComponent;
    modal['submit']();
    expect(api.create).not.toHaveBeenCalled();
    modal['code'] = 'new-1';
    modal['name'] = 'Chỉ số mới';
    modal['unit'] = 'VND';
    modal['submit']();
    expect(api.create).toHaveBeenCalled();
    expect(api.create.calls.mostRecent().args[0].code).toBe('NEW-1');
  });
});
