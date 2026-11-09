import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import {
  EvaluationProfileDetail,
  EvaluationProfileItem,
} from '../../core/evaluation-profile.models';
import { EvaluationProfileFormDialogComponent } from './evaluation-profile-form-dialog.component';
import { ProfileRow } from './evaluation-profile-tree';

const api = environment.apiUrl;

const leaf = (kpiDefinitionId: string, weightPercent: number): EvaluationProfileItem => ({
  id: `leaf-${kpiDefinitionId}`,
  parentItemId: 'g1',
  name: null,
  kpiDefinitionId,
  kpiCode: kpiDefinitionId.toUpperCase(),
  kpiName: `KPI ${kpiDefinitionId}`,
  weightPercent,
  displayOrder: 1,
  children: [],
});

const stored = (over: Partial<EvaluationProfileDetail> = {}): EvaluationProfileDetail => ({
  id: 'p1',
  code: 'EP-TELLER',
  name: 'Teller profile',
  description: 'About',
  jobPositionId: 'j1',
  jobPositionCode: 'TELLER',
  jobPositionTitle: 'Teller',
  version: 1,
  effectiveFrom: '2026-10-01',
  effectiveTo: '2026-12-31',
  status: 'DRAFT',
  rejectReason: null,
  isInUse: false,
  totalWeightPercent: 100,
  updatedAt: '2026-10-23T07:00:00+00:00',
  items: [
    {
      id: 'g1',
      parentItemId: null,
      name: 'Sales',
      kpiDefinitionId: null,
      kpiCode: null,
      kpiName: null,
      weightPercent: 100,
      displayOrder: 1,
      children: [leaf('k1', 60), leaf('k9', 40)],
    },
  ],
  ...over,
});

/** The protected members the specs drive directly. */
interface FormInternals {
  loading(): boolean;
  loadFailed(): boolean;
  saving(): boolean;
  saveError(): string | null;
  rows(): ProfileRow[];
  treeSupported(): boolean;
  kpis(): { id: string }[];
  positions(): { id: string }[];
  isEdit: boolean;
  structureLocked: boolean;
  name: string;
  code: string;
  jobPositionId: string;
  effectiveFrom: string;
  effectiveTo: string;
  addRow(): void;
  removeRow(index: number): void;
  setGroup(index: number, group: string): void;
  setKpi(index: number, kpiDefinitionId: string): void;
  setWeight(index: number, weight: number | null): void;
  kpiOptions(index: number): { id: string }[];
  total(): number;
  periodInvalid(): boolean;
  valid(): boolean;
  changeText(): string;
  save(): void;
}

describe('EvaluationProfileFormDialogComponent', () => {
  const kpiPage = {
    success: true,
    data: {
      items: [
        { id: 'k1', code: 'K1', name: 'KPI k1', status: 'ACTIVE' },
        { id: 'k2', code: 'K2', name: 'KPI k2', status: 'ACTIVE' },
        { id: 'k3', code: 'K3', name: 'KPI k3', status: 'INACTIVE' },
      ],
      totalCount: 3,
      page: 1,
      pageSize: 100,
    },
  };
  const positions = { success: true, data: [{ id: 'j1', code: 'TELLER', title: 'Teller' }] };

  function setup(profileId: string | null, profile?: EvaluationProfileDetail | 'error') {
    TestBed.configureTestingModule({
      imports: [EvaluationProfileFormDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(EvaluationProfileFormDialogComponent);
    (fixture.componentRef as ComponentRef<EvaluationProfileFormDialogComponent>).setInput(
      'profileId',
      profileId,
    );
    const saved = jasmine.createSpy('saved');
    const dismissed = jasmine.createSpy('dismissed');
    fixture.componentInstance.saved.subscribe(saved);
    fixture.componentInstance.dismissed.subscribe(dismissed);
    fixture.detectChanges();

    http.expectOne((r) => r.url === `${api}/kpi-definitions`).flush(kpiPage);
    if (profileId) {
      const req = http.expectOne(`${api}/evaluation-profiles/${profileId}`);
      if (profile === 'error') req.flush({}, { status: 500, statusText: 'Server Error' });
      else req.flush({ success: true, data: profile ?? stored() });
    } else {
      http.expectOne(`${api}/employees/job-positions`).flush(positions);
    }
    fixture.detectChanges();
    const component = fixture.componentInstance as unknown as FormInternals;
    return { http, fixture, component, saved, dismissed };
  }

  it('creates: loads the active KPIs and job positions and starts with no rows', () => {
    const { http, component } = setup(null);

    expect(component.isEdit).toBeFalse();
    expect(component.loading()).toBeFalse();
    expect(component.kpis().map((kpi) => kpi.id)).toEqual(['k1', 'k2']);
    expect(component.positions().map((position) => position.id)).toEqual(['j1']);
    expect(component.rows()).toEqual([]);
    http.verify();
  });

  it('edits: fills the form from the stored profile and keeps a deactivated KPI selectable', () => {
    const { http, component } = setup('p1');

    expect(component.isEdit).toBeTrue();
    expect(component.name).toBe('Teller profile');
    expect(component.rows().map((row) => [row.group, row.kpiDefinitionId, row.weight])).toEqual([
      ['Sales', 'k1', 60],
      ['Sales', 'k9', 40],
    ]);
    expect(component.kpis().map((kpi) => kpi.id)).toEqual(['k1', 'k2', 'k9']);
    http.verify();
  });

  it('shows the load error and does not save when the profile cannot be loaded', () => {
    const { http, component } = setup('p1', 'error');

    expect(component.loadFailed()).toBeTrue();
    expect(component.loading()).toBeFalse();

    component.save();
    http.expectNone(`${api}/evaluation-profiles/p1`);
  });

  it('locks the rows of a profile that is already in use', () => {
    const { component } = setup('p1', stored({ isInUse: true }));

    expect(component.structureLocked).toBeTrue();
  });

  it('refuses a tree the two-level form cannot rebuild', () => {
    const { component } = setup(
      'p1',
      stored({ items: [{ ...stored().items[0], kpiDefinitionId: 'k1', name: null }] }),
    );

    expect(component.treeSupported()).toBeFalse();
    expect(component.valid()).toBeFalse();
  });

  it('offers each KPI to one row only', () => {
    const { component } = setup(null);
    component.addRow();
    component.addRow();
    component.setKpi(0, 'k1');

    expect(component.kpiOptions(0).map((kpi) => kpi.id)).toEqual(['k1', 'k2']);
    expect(component.kpiOptions(1).map((kpi) => kpi.id)).toEqual(['k2']);
  });

  it('adds and removes rows and adds up the weights', () => {
    const { component } = setup(null);
    component.addRow();
    component.addRow();
    component.setWeight(0, 60);
    component.setWeight(1, 40);

    expect(component.total()).toBe(100);

    component.removeRow(0);
    expect(component.rows().length).toBe(1);
    expect(component.total()).toBe(40);
  });

  it('needs a name, code, position, start date and complete rows before a new profile is valid', () => {
    const { component } = setup(null);
    expect(component.valid()).toBeFalse();

    component.name = 'Teller';
    component.code = 'EP-TELLER';
    component.jobPositionId = 'j1';
    component.effectiveFrom = '2026-10-01';
    expect(component.valid()).toBeTrue();

    component.addRow();
    expect(component.valid()).toBeFalse();

    component.setGroup(0, 'Sales');
    component.setKpi(0, 'k1');
    component.setWeight(0, 100);
    expect(component.valid()).toBeTrue();

    component.setWeight(0, 101);
    expect(component.valid()).toBeFalse();
    component.setWeight(0, 0);
    expect(component.valid()).toBeFalse();
  });

  it('rejects an end date before the start date', () => {
    const { component } = setup(null);
    component.name = 'Teller';
    component.code = 'EP-TELLER';
    component.jobPositionId = 'j1';
    component.effectiveFrom = '2026-10-01';
    component.effectiveTo = '2026-09-30';

    expect(component.periodInvalid()).toBeTrue();
    expect(component.valid()).toBeFalse();
  });

  it('creates with the trimmed code and name and the rebuilt tree, then reports it saved', () => {
    const { http, component, saved } = setup(null);
    component.name = '  Teller  ';
    component.code = ' EP-TELLER ';
    component.jobPositionId = 'j1';
    component.effectiveFrom = '2026-10-01';
    component.addRow();
    component.setGroup(0, 'Sales');
    component.setKpi(0, 'k1');
    component.setWeight(0, 100);

    component.save();
    const req = http.expectOne(`${api}/evaluation-profiles`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.code).toBe('EP-TELLER');
    expect(req.request.body.name).toBe('Teller');
    expect(req.request.body.effectiveTo).toBeNull();
    expect(req.request.body.items[0].children[0].kpiDefinitionId).toBe('k1');
    expect(component.saving()).toBeTrue();
    req.flush({ success: true, data: stored() });

    expect(component.saving()).toBeFalse();
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it('updates an existing profile with PUT and keeps its description', () => {
    const { http, component } = setup('p1');
    component.name = 'Renamed';

    component.save();
    const req = http.expectOne(`${api}/evaluation-profiles/p1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.name).toBe('Renamed');
    expect(req.request.body.description).toBe('About');
    expect(req.request.body.code).toBeUndefined();
    req.flush({ success: true, data: stored() });
  });

  it('shows the message of a failed save and stays open', () => {
    const { http, component, saved } = setup('p1');

    component.save();
    http
      .expectOne(`${api}/evaluation-profiles/p1`)
      .flush(
        { success: false, message: 'Profile is in use.' },
        { status: 409, statusText: 'Conflict' },
      );

    expect(component.saveError()).toBe('Profile is in use.');
    expect(component.saving()).toBeFalse();
    expect(saved).not.toHaveBeenCalled();
  });

  it('does not send a second save while one is running', () => {
    const { http, component } = setup('p1');

    component.save();
    component.save();

    expect(http.match(`${api}/evaluation-profiles/p1`).length).toBe(1);
    expect(component.saving()).toBeTrue();
  });

  it('counts the name and row changes of an edit', () => {
    const { component } = setup('p1');
    expect(component.changeText()).toContain('0');

    component.name = 'Renamed';
    component.setWeight(0, 50);

    expect(component.changeText()).toContain('1');
  });
});
