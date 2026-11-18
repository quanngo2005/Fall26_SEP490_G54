import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { I18nService } from '../../core/i18n/i18n.service';
import { ReconciliationFlagModalComponent } from './reconciliation-flag-modal.component';

describe('ReconciliationFlagModalComponent', () => {
  let http: HttpTestingController;

  const open = () => {
    const fixture = TestBed.createComponent(ReconciliationFlagModalComponent);
    fixture.componentRef.setInput('submissionId', 's1');
    fixture.detectChanges();
    return fixture;
  };
  const type = (fixture: { nativeElement: HTMLElement }, value: string) => {
    const box = fixture.nativeElement.querySelector('#flag-reason') as HTMLTextAreaElement;
    box.value = value;
    box.dispatchEvent(new Event('input'));
  };
  const submit = (fixture: { nativeElement: HTMLElement }) =>
    (
      fixture.nativeElement.querySelector('.modal-footer .button-primary') as HTMLButtonElement
    ).click();

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReconciliationFlagModalComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).setLanguage('vi');
  });

  afterEach(() => http.verify());

  it('asks for a reason of at least 10 characters before anything is sent', () => {
    const fixture = open();
    type(fixture, 'ngắn');

    submit(fixture);
    fixture.detectChanges();

    http.expectNone(`${environment.apiUrl}/submissions/s1/quality-flags`);
    expect(fixture.nativeElement.textContent).toContain('Nhập lý do ít nhất 10 ký tự.');
  });

  it('flags the approved submission with the chosen severity', () => {
    const fixture = open();
    let flagged = 0;
    fixture.componentInstance.flagged.subscribe(() => flagged++);
    type(fixture, 'Chứng từ không khớp với báo cáo.');
    const severity = fixture.nativeElement.querySelector('#flag-severity') as HTMLSelectElement;
    severity.value = 'HIGH';
    severity.dispatchEvent(new Event('change'));

    submit(fixture);

    const request = http.expectOne(`${environment.apiUrl}/submissions/s1/quality-flags`);
    expect(request.request.body).toEqual({
      reason: 'Chứng từ không khớp với báo cáo.',
      severity: 'HIGH',
    });
    request.flush({ success: true, data: {} });
    expect(flagged).toBe(1);
  });

  it('keeps the form open and explains when the flag could not be created', () => {
    const fixture = open();
    let flagged = 0;
    fixture.componentInstance.flagged.subscribe(() => flagged++);
    type(fixture, 'Chứng từ không khớp với báo cáo.');

    submit(fixture);
    http
      .expectOne(`${environment.apiUrl}/submissions/s1/quality-flags`)
      .flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(flagged).toBe(0);
    expect(fixture.nativeElement.textContent).toContain('Không gắn được cờ. Vui lòng thử lại.');
  });

  it('closes on Escape', () => {
    const fixture = open();
    let dismissed = 0;
    fixture.componentInstance.dismissed.subscribe(() => dismissed++);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(dismissed).toBe(1);
  });
});
