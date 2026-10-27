import { ComponentFixture, TestBed } from '@angular/core/testing';
import { I18nService } from '../../core/i18n/i18n.service';
import { EmployeeDetailDrawerComponent } from './employee-detail-drawer.component';
import { EmployeeListItem } from './employee-management.models';

const MOCK_EMPLOYEE: EmployeeListItem = {
  id: 'emp-0012',
  code: 'NV0012',
  fullName: 'Nguyễn Văn A',
  departmentCode: 'PGD-01',
  departmentName: 'Phòng Giao dịch 1',
  positionCode: 'GDV',
  positionTitle: 'Giao dịch viên',
  status: 'ACTIVE',
  account: 'nva.teller',
  managerName: 'Lê Văn C',
  joinDate: '15/03/2022',
  email: 'nva@bank.vn',
  phone: '0912 345 678',
  lastSyncAt: '24/10/2026 08:15',
  latestLockedEvaluation: {
    period: 'Q3/2026',
    tier: 'Tier A1',
  },
  latestImportBatch: {
    batchNumber: '123',
    timestamp: '24/10/2026 08:15',
    changeStatus: 'không thay đổi',
  },
};

describe('EmployeeDetailDrawerComponent', () => {
  let fixture: ComponentFixture<EmployeeDetailDrawerComponent>;
  let component: EmployeeDetailDrawerComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EmployeeDetailDrawerComponent],
      providers: [I18nService],
    }).compileComponents();

    fixture = TestBed.createComponent(EmployeeDetailDrawerComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('employee', MOCK_EMPLOYEE);
    fixture.detectChanges();
  });

  it('renders employee code and full name in the title', () => {
    const titleEl: HTMLElement = fixture.nativeElement.querySelector('.employee-title');
    expect(titleEl.textContent).toContain('NV0012');
    expect(titleEl.textContent).toContain('Nguyễn Văn A');
  });

  it('renders active status badge for active employee', () => {
    const badgeEl: HTMLElement = fixture.nativeElement.querySelector('.badge');
    expect(badgeEl.classList).toContain('badge-active');
  });

  it('renders information grid with department, manager, email, phone', () => {
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('PGD-01 · Phòng Giao dịch 1');
    expect(text).toContain('Giao dịch viên');
    expect(text).toContain('Lê Văn C');
    expect(text).toContain('nva@bank.vn');
    expect(text).toContain('0912 345 678');
  });

  it('renders locked evaluation info and link', () => {
    const evalText: HTMLElement = fixture.nativeElement.querySelector('.eval-text');
    expect(evalText.textContent).toContain('Q3/2026');
    expect(evalText.textContent).toContain('Tier A1');
  });

  it('emits closeDrawer when close button is clicked', () => {
    spyOn(component.closeDrawer, 'emit');
    const closeBtn: HTMLButtonElement = fixture.nativeElement.querySelector('.close-button');
    closeBtn.click();
    expect(component.closeDrawer.emit).toHaveBeenCalled();
  });

  it('emits closeDrawer when Escape key is pressed', () => {
    spyOn(component.closeDrawer, 'emit');
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(component.closeDrawer.emit).toHaveBeenCalled();
  });
});
