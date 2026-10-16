import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SAMPLE_DEPARTMENTS } from './department-detail.data';
import { DepartmentDetailComponent } from './department-detail.component';

describe('DepartmentDetailComponent', () => {
  const sample = SAMPLE_DEPARTMENTS[0];
  const url = (id: string) => `${environment.apiUrl}/v1/departments/${id}/detail`;
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let router: jasmine.SpyObj<Router>;

  function create() {
    const fixture = TestBed.createComponent(DepartmentDetailComponent);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({ departmentId: sample.id }));
    router = jasmine.createSpyObj<Router>('Router', ['navigate', 'navigateByUrl']);
    TestBed.configureTestingModule({
      imports: [DepartmentDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
        { provide: Router, useValue: router },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shows a skeleton, then department data', () => {
    const fixture = create();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[aria-busy="true"]')).not.toBeNull();

    http.expectOne(url(sample.id)).flush(sample);
    fixture.detectChanges();
    expect(el.querySelector('h1')?.textContent).toContain(sample.name);
    expect(el.querySelector('[aria-busy="true"]')).toBeNull();
  });

  it('shows access restricted for 403 without exposing department data', () => {
    const fixture = create();
    http.expectOne(url(sample.id)).flush({}, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Truy cập bị hạn chế');
    expect(text).not.toContain(sample.name);
  });

  it('shows not found for 404', () => {
    const fixture = create();
    http.expectOne(url(sample.id)).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Không tìm thấy phòng ban',
    );
  });

  it('navigates to a child department with its id as context', () => {
    const fixture = create();
    http.expectOne(url(sample.id)).flush(sample);
    fixture.detectChanges();

    const child = (fixture.nativeElement as HTMLElement).querySelector(
      '.child',
    ) as HTMLButtonElement;
    child.click();
    expect(router.navigate).toHaveBeenCalledWith(['/departments', sample.children[0].id], {
      queryParams: {},
    });
  });

  it('filters the employee preview by search term', () => {
    const fixture = create();
    http.expectOne(url(sample.id)).flush(sample);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const rows = () => el.querySelectorAll('app-data-table tbody tr').length;
    expect(rows()).toBe(sample.employeePreview.length);

    fixture.componentInstance['onSearch']('mai');
    fixture.detectChanges();
    expect(rows()).toBe(1);
  });

  it('reloads when the route param changes', () => {
    const fixture = create();
    http.expectOne(url(sample.id)).flush(sample);
    fixture.detectChanges();

    params.next(convertToParamMap({ departmentId: 'other' }));
    fixture.detectChanges();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[aria-busy="true"]'),
    ).not.toBeNull();
    http.expectOne(url('other')).flush({}, { status: 404, statusText: 'Not Found' });
  });
});
