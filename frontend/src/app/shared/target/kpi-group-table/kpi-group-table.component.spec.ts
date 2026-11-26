import { TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { I18nService } from '../../../core/i18n/i18n.service';
import { KpiGroupTableComponent } from './kpi-group-table.component';
import { KpiGroupTableGroup } from '../target-shared.models';

const groups: KpiGroupTableGroup[] = [
  {
    id: 'g1',
    name: 'Huy động vốn',
    complete: true,
    rows: [{ id: 'k1', kpiName: 'Tiền gửi kỳ hạn mới', kpiType: 'SUM' }],
  },
  {
    id: 'g2',
    name: 'Tín dụng',
    complete: false,
    rows: [{ id: 'k2', kpiName: 'Dư nợ tín dụng', kpiType: 'BALANCE' }],
  },
];

@Component({
  standalone: true,
  imports: [KpiGroupTableComponent],
  template: `
    <app-kpi-group-table [groups]="groups" [rowTemplate]="cols" [extraColumnCount]="1" />
<ng-template #cols let-row let-group="group">
  <td class="value-cell">{{row.kpiName}}@{{group.name}}</td>
</ng-template>
  `,
})
class HostComponent {
  groups = groups;
}

describe('KpiGroupTableComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: I18nService,
          useValue: jasmine.createSpyObj('I18nService', [], {
            localization: new I18nService().localization,
          }),
        },
      ],
    });
  });

  it('starts a complete group collapsed and an incomplete group open', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const groupRows = element.querySelectorAll('.group-row');
    expect(groupRows[0].textContent).toContain('Huy động vốn');
    expect(groupRows[0].querySelector('.complete-badge')).not.toBeNull();
    expect(element.querySelectorAll('.kpi-row').length).toBe(1);
    expect(element.querySelector('.kpi-row')?.textContent).toContain('Dư nợ tín dụng');
  });

  it('toggles a group open/closed on click and projects the custom column', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const groupRows = element.querySelectorAll<HTMLElement>('.group-row');

    groupRows[0].click();
    fixture.detectChanges();
    expect(element.querySelectorAll('.kpi-row').length).toBe(2);
    expect(element.querySelector('.value-cell')?.textContent).toContain(
      'Tiền gửi kỳ hạn mới@Huy động vốn',
    );

    groupRows[1].click();
    fixture.detectChanges();
    expect(element.querySelectorAll('.kpi-row').length).toBe(1);
  });
});
