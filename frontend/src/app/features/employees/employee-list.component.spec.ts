import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { I18nService } from '../../core/i18n/i18n.service';
import { EmployeeListComponent } from './employee-list.component';

describe('EmployeeListComponent', () => {
  let fixture: ComponentFixture<EmployeeListComponent>;
  let component: EmployeeListComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EmployeeListComponent],
      providers: [provideRouter([]), provideHttpClient(), I18nService],
    }).compileComponents();

    fixture = TestBed.createComponent(EmployeeListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders page heading with title and read-only badge', () => {
    const h1: HTMLElement = fixture.nativeElement.querySelector('h1');
    const badge: HTMLElement = fixture.nativeElement.querySelector('.read-only-badge');
    expect(h1).toBeTruthy();
    expect(badge).toBeTruthy();
  });

  it('renders sync banner with time and SA source', () => {
    const banner: HTMLElement = fixture.nativeElement.querySelector('.sync-banner');
    expect(banner.textContent).toContain('24/10/2026 08:15');
    expect(banner.textContent).toContain('SA');
  });

  it('renders filter toolbar with search and dropdowns', () => {
    const searchInput = fixture.nativeElement.querySelector('.search-input');
    const dropdowns = fixture.nativeElement.querySelectorAll('.dropdown-select');
    expect(searchInput).toBeTruthy();
    expect(dropdowns.length).toBe(3);
  });

  it('renders table rows with sample employees', () => {
    const rows = fixture.nativeElement.querySelectorAll('.employee-row');
    expect(rows.length).toBe(5);
  });

  it('selects an employee row and opens drawer', () => {
    const rows = fixture.nativeElement.querySelectorAll('.employee-row');
    rows[1].click();
    fixture.detectChanges();

    const drawer = fixture.nativeElement.querySelector('.employee-detail-drawer');
    expect(drawer).toBeTruthy();
    expect(drawer.textContent).toContain('NV0031');
  });

  it('filters employees by keyword', () => {
    component['keyword'].set('Ngô Thị K');
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('.employee-row');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('NV0003');
  });

  it('closes drawer when closeDrawer is called', () => {
    component.closeDrawer();
    fixture.detectChanges();

    const drawer = fixture.nativeElement.querySelector('.employee-detail-drawer');
    expect(drawer).toBeNull();
  });

  it('navigates through employees with ArrowDown key', () => {
    component['selectedEmployeeId'].set('emp-0012');
    fixture.detectChanges();

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    fixture.detectChanges();

    expect(component['selectedEmployeeId']()).toBe('emp-0031');
  });
});
