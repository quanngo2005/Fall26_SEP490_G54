import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DEFAULT_EMPLOYEE_RESPONSE } from './employee-list.data';
import { EmployeeListComponent } from './employee-list.component';

describe('EmployeeListComponent', () => {
  let component: EmployeeListComponent;
  let fixture: ComponentFixture<EmployeeListComponent>;
  let http: HttpTestingController;
  let router: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);

    await TestBed.configureTestingModule({
      imports: [EmployeeListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            queryParamMap: of(convertToParamMap({})),
          },
        },
        { provide: Router, useValue: router },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EmployeeListComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    http.verify();
  });

  it('should create and load employee list from API', () => {
    fixture.detectChanges();

    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/v1/employees`);
    expect(req.request.method).toBe('GET');
    req.flush(DEFAULT_EMPLOYEE_RESPONSE);

    fixture.detectChanges();
    expect(component).toBeTruthy();
    expect(component['viewState']()).toBe('success');
    expect(component['data']()?.total).toBe(42);

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h1')?.textContent).toContain('Danh sách Cán bộ Nhân viên');
    expect(el.querySelectorAll('.summary-card').length).toBe(4);
  });

  it('should handle API error gracefully', () => {
    fixture.detectChanges();

    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/v1/employees`);
    req.flush({}, { status: 500, statusText: 'Internal Server Error' });

    fixture.detectChanges();
    expect(component['viewState']()).toBe('error');
    expect(component['errorMessage']()).toContain('500');
  });

  it('should navigate to employee profile on row click', () => {
    fixture.detectChanges();
    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/v1/employees`);
    req.flush(DEFAULT_EMPLOYEE_RESPONSE);
    fixture.detectChanges();

    component['navigateToEmployee']('emp-00182');
    expect(router.navigate).toHaveBeenCalledWith(['/employee-profile'], {
      queryParams: {
        employeeId: 'emp-00182',
        departmentId: undefined,
      },
    });
  });

  it('should filter when position chip is clicked', () => {
    fixture.detectChanges();
    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/v1/employees`);
    req.flush(DEFAULT_EMPLOYEE_RESPONSE);
    fixture.detectChanges();

    component['selectPositionChip']('pos-teller');
    expect(component['selectedPosition']()).toBe('pos-teller');
    expect(router.navigate).toHaveBeenCalled();
  });

  it('should open and close the add employee modal', () => {
    fixture.detectChanges();
    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/v1/employees`);
    req.flush(DEFAULT_EMPLOYEE_RESPONSE);
    fixture.detectChanges();

    expect(component['isAddModalOpen']()).toBeFalse();
    component['openAddModal']();
    expect(component['isAddModalOpen']()).toBeTrue();
    component['closeAddModal']();
    expect(component['isAddModalOpen']()).toBeFalse();
  });

  it('should debounce search input', fakeAsync(() => {
    fixture.detectChanges();
    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/v1/employees`);
    req.flush(DEFAULT_EMPLOYEE_RESPONSE);
    fixture.detectChanges();

    component['searchInput$'].next('Nguyễn');
    tick(100);
    expect(component['search']()).toBe('');

    tick(150);
    expect(component['search']()).toBe('Nguyễn');

    const searchReq = http.expectOne((r) => r.url === `${environment.apiUrl}/v1/employees`);
    expect(searchReq.request.params.get('search')).toBe('Nguyễn');
    searchReq.flush(DEFAULT_EMPLOYEE_RESPONSE);
  }));
});
