import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';
import {
  INITIAL_MOCK_GROUPS,
  INITIAL_MOCK_KPIS,
  KpiDefinitionService,
} from '../../core/kpi-definition.service';
import { KpiCreateModalComponent } from './kpi-create-modal/kpi-create-modal.component';
import { KpiDefinitionsComponent } from './kpi-definitions.component';

describe('KpiDefinitionsComponent', () => {
  let component: KpiDefinitionsComponent;
  let fixture: ComponentFixture<KpiDefinitionsComponent>;
  let mockService: jasmine.SpyObj<KpiDefinitionService>;

  beforeEach(async () => {
    mockService = jasmine.createSpyObj<KpiDefinitionService>('KpiDefinitionService', [
      'getKpis',
      'getGroups',
      'getKpiById',
      'createKpi',
      'updateKpi',
      'changeStatus',
    ]);

    mockService.getKpis.and.returnValue(
      of({
        success: true,
        status: 200,
        data: {
          items: [...INITIAL_MOCK_KPIS],
          totalCount: INITIAL_MOCK_KPIS.length,
          page: 1,
          pageSize: 10,
        },
      }),
    );

    mockService.getGroups.and.returnValue(
      of({
        success: true,
        status: 200,
        data: [...INITIAL_MOCK_GROUPS],
      }),
    );

    await TestBed.configureTestingModule({
      imports: [KpiDefinitionsComponent],
      providers: [{ provide: KpiDefinitionService, useValue: mockService }],
    }).compileComponents();

    fixture = TestBed.createComponent(KpiDefinitionsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create the component and load initial KPIs', () => {
    expect(component).toBeTruthy();
    expect(component.kpis().length).toBe(3);
    expect(component.selectedKpi()?.code).toBe('KPI-RET-01');
    expect(component.editCode()).toBe('KPI-RET-01');
    expect(component.editName()).toBe('Tổng số dư huy động mới');
  });

  it('should filter KPIs based on searchTerm and group', () => {
    component.searchTerm.set('lỗi');
    expect(component.filteredKpis().length).toBe(1);
    expect(component.filteredKpis()[0].code).toBe('KPI-OP-02');

    component.searchTerm.set('');
    component.groupFilter.set('grp-002');
    expect(component.filteredKpis().length).toBe(1);
    expect(component.filteredKpis()[0].kpiGroupName).toBe('Vận hành');
  });

  it('should calculate role weights and validate BR-06 rule (100%)', () => {
    // Default weights: Teller 40 + RBO 60 = 100
    expect(component.totalRoleWeight()).toBe(100);
    expect(component.isWeightValid()).toBeTrue();

    // Change first weight to 30 => sum = 90
    component.updateRoleWeightValue(0, 30);
    expect(component.totalRoleWeight()).toBe(90);
    expect(component.isWeightValid()).toBeFalse();

    // Change second weight to 70 => sum = 100
    component.updateRoleWeightValue(1, 70);
    expect(component.totalRoleWeight()).toBe(100);
    expect(component.isWeightValid()).toBeTrue();
  });

  it('should select another KPI and update form values', () => {
    const kpi2 = component.kpis()[1];
    component.selectKpi(kpi2);

    expect(component.selectedKpi()?.id).toBe(kpi2.id);
    expect(component.editCode()).toBe(kpi2.code);
    expect(component.editName()).toBe(kpi2.name);
    expect(component.editDirection()).toBe('LowerBetter');
  });

  it('should save KPI configuration when valid', () => {
    const targetKpi = component.selectedKpi()!;
    mockService.updateKpi.and.returnValue(
      of({
        success: true,
        status: 200,
        data: {
          ...targetKpi,
          name: 'Tên KPI Đã Cập Nhật',
        },
      }),
    );

    component.editName.set('Tên KPI Đã Cập Nhật');
    component.saveConfig();

    expect(mockService.updateKpi).toHaveBeenCalled();
    expect(component.selectedKpi()?.name).toBe('Tên KPI Đã Cập Nhật');
    expect(component.toast()?.type).toBe('success');
  });

  it('should open modal and create a new KPI definition', () => {
    mockService.createKpi.and.returnValue(
      of({
        success: true,
        status: 201,
        data: {
          id: 'new-kpi-id',
          code: 'KPI-NEW-99',
          name: 'Chỉ số mới',
          kpiGroupId: 'grp-001',
          kpiGroupName: 'Kinh doanh',
          description: 'Mô tả',
          formula: null,
          unit: 'VND',
          aggregationType: 'Sum',
          direction: 'HigherBetter',
          maxAchievementPercent: 100,
          status: 'Active',
          updatedAt: new Date().toISOString(),
        },
      }),
    );

    component.openCreateModal();
    expect(component.isCreateModalOpen()).toBeTrue();

    fixture.detectChanges();

    const modal = fixture.debugElement.query(By.directive(KpiCreateModalComponent))
      .componentInstance as KpiCreateModalComponent;
    modal.code.set('KPI-NEW-99');
    modal.name.set('Chỉ số mới');
    modal.groupId.set('grp-001');

    modal.submit();
    fixture.detectChanges();

    expect(mockService.createKpi).toHaveBeenCalled();
    expect(component.isCreateModalOpen()).toBeFalse();
    expect(component.kpis().some((k) => k.code === 'KPI-NEW-99')).toBeTrue();
    expect(component.selectedKpi()?.code).toBe('KPI-NEW-99');
  });

  it('should reject a duplicate KPI code in the create modal', () => {
    component.openCreateModal();
    fixture.detectChanges();

    const modal = fixture.debugElement.query(By.directive(KpiCreateModalComponent))
      .componentInstance as KpiCreateModalComponent;
    modal.code.set('KPI-RET-01');
    modal.name.set('Trùng mã');

    modal.submit();

    expect(mockService.createKpi).not.toHaveBeenCalled();
    expect(modal.error()).toContain('KPI-RET-01');
    expect(component.isCreateModalOpen()).toBeTrue();
  });
});
