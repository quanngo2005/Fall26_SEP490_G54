import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { KpiDefinition } from '../../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../../core/kpi-definition.service';
import { KpiEditModalComponent } from './kpi-edit-modal.component';

describe('KpiEditModalComponent', () => {
  let component: KpiEditModalComponent;
  let fixture: ComponentFixture<KpiEditModalComponent>;
  let api: jasmine.SpyObj<KpiDefinitionService>;

  const mockKpi: KpiDefinition = {
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
    measurementType: 'Quantitative',
    applyProductMultiplier: true,
    depositMultipliers: [{ id: 'm-1', label: 'CASA', value: 1.2 }],
    roleWeights: [{ id: 'rw-1', role: 'Teller', weight: 100 }],
  };

  beforeEach(async () => {
    api = jasmine.createSpyObj<KpiDefinitionService>('KpiDefinitionService', ['update']);
    api.update.and.returnValue(of(mockKpi));

    await TestBed.configureTestingModule({
      imports: [KpiEditModalComponent],
      providers: [{ provide: KpiDefinitionService, useValue: api }],
    }).compileComponents();

    fixture = TestBed.createComponent(KpiEditModalComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('kpi', mockKpi);
    fixture.componentRef.setInput('groups', [{ id: mockKpi.kpiGroupId, name: 'Kinh doanh' }]);
    fixture.componentRef.setInput('units', ['tỷ VND', '%']);
    fixture.detectChanges();
  });

  it('should initialize form fields from input kpi', () => {
    expect(component.name()).toBe(mockKpi.name);
    expect(component.unit()).toBe(mockKpi.unit);
    expect(component.groupId()).toBe(mockKpi.kpiGroupId);
    expect(component.measurementType()).toBe('Quantitative');
    expect(component.applyMultiplier()).toBeTrue();
    expect(component.multipliers().length).toBe(1);
    expect(component.roleWeights().length).toBe(1);
    expect(component.isWeightValid()).toBeTrue();
  });

  it('should handle product multiplier operations', () => {
    component.addMultiplier();
    expect(component.multipliers().length).toBe(2);

    component.updateMultiplierLabel(1, 'Deposit');
    expect(component.multipliers()[1].label).toBe('Deposit');

    component.updateMultiplierValue(1, 1.5);
    expect(component.multipliers()[1].value).toBe(1.5);

    component.stepMultiplier(1, 1);
    expect(component.multipliers()[1].value).toBe(1.6);

    component.stepMultiplier(1, -1);
    expect(component.multipliers()[1].value).toBe(1.5);

    component.removeMultiplier(1);
    expect(component.multipliers().length).toBe(1);
  });

  it('should handle role weights operations and validation', () => {
    component.addRole();
    expect(component.roleWeights().length).toBe(2);

    component.updateRoleName(1, 'Officer');
    expect(component.roleWeights()[1].role).toBe('Officer');

    component.updateRoleWeight(0, 60);
    component.updateRoleWeight(1, 30);
    expect(component.totalWeight()).toBe(90);
    expect(component.isWeightValid()).toBeFalse();

    component.updateRoleWeight(1, 40);
    expect(component.totalWeight()).toBe(100);
    expect(component.isWeightValid()).toBeTrue();

    component.removeRole(1);
    expect(component.roleWeights().length).toBe(1);
  });

  it('should validate required fields before saving', () => {
    component.name.set('');
    component.save();
    expect(component.error()).toBeTruthy();
    expect(api.update).not.toHaveBeenCalled();

    component.name.set('Valid Name');
    component.unit.set('');
    component.save();
    expect(component.error()).toBeTruthy();
    expect(api.update).not.toHaveBeenCalled();

    component.unit.set('%');
    component.updateRoleWeight(0, 50); // total 50 != 100
    component.save();
    expect(component.error()).toBeTruthy();
    expect(api.update).not.toHaveBeenCalled();
  });

  it('should call api.update and emit saved event on success', () => {
    component.updateRoleWeight(0, 100);
    spyOn(component.saved, 'emit');

    component.save();

    expect(api.update).toHaveBeenCalledWith(
      mockKpi.id,
      jasmine.objectContaining({
        name: mockKpi.name,
        unit: mockKpi.unit,
      }),
    );
    expect(component.saved.emit).toHaveBeenCalled();
  });

  it('should handle save error', () => {
    api.update.and.returnValue(throwError(() => new Error('Save failed')));
    component.updateRoleWeight(0, 100);

    component.save();

    expect(component.saving()).toBeFalse();
    expect(component.error()).toBeTruthy();
  });

  it('should emit closed when close is called and not saving', () => {
    spyOn(component.closed, 'emit');
    component.close();
    expect(component.closed.emit).toHaveBeenCalled();
  });
});
