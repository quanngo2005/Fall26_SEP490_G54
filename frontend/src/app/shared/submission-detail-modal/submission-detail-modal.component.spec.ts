import { ComponentFixture, TestBed } from '@angular/core/testing';
import { I18nService } from '../../core/i18n/i18n.service';
import {
  defaultSubmission,
  SubmissionDetailModalComponent,
} from './submission-detail-modal.component';

describe('SubmissionDetailModalComponent', () => {
  let component: SubmissionDetailModalComponent;
  let fixture: ComponentFixture<SubmissionDetailModalComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SubmissionDetailModalComponent],
      providers: [I18nService],
    }).compileComponents();

    fixture = TestBed.createComponent(SubmissionDetailModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create the component', () => {
    expect(component).toBeTruthy();
  });

  it('should not render dialog when open is false', () => {
    const dialog = fixture.nativeElement.querySelector('.submission-dialog');
    expect(dialog).toBeNull();
  });

  it('should render dialog when open is true', () => {
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    const dialog = fixture.nativeElement.querySelector('.submission-dialog');
    expect(dialog).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain(defaultSubmission.submissionId);
  });

  it('should emit closed event on close button click', () => {
    let closed = false;
    component.closed.subscribe(() => (closed = true));
    component.close();
    expect(closed).toBeTrue();
  });
});
