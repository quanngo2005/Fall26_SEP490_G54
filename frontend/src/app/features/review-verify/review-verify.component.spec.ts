import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { SidebarConfigurationService } from '../../core/sidebar-configuration.service';
import { environment } from '../../../environments/environment';
import { ReviewVerifyComponent } from './review-verify.component';
import { DEFAULT_MOCK_DOSSIER } from './review-verify.data';
import { ReviewVerifyService } from './review-verify.service';

describe('ReviewVerifyComponent', () => {
  let component: ReviewVerifyComponent;
  let fixture: ComponentFixture<ReviewVerifyComponent>;
  let http: HttpTestingController;
  const user = signal({
    id: 'emp-mgr-001',
    email: 'manager@example.test',
    fullName: 'Trần Văn Quản Lý',
    roles: ['MANAGER'],
    permissions: ['VERIFICATION_DECIDE'],
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReviewVerifyComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        I18nService,
        ReviewVerifyService,
        {
          provide: AuthService,
          useValue: { currentUser: user.asReadonly(), logout: () => of(null) },
        },
        {
          provide: SidebarConfigurationService,
          useValue: { current: () => of([]) },
        },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReviewVerifyComponent);
    component = fixture.componentInstance;

    fixture.detectChanges();
    const req = http.expectOne(
      `${environment.apiUrl}/verifications/${DEFAULT_MOCK_DOSSIER.submissionId}/dossier`,
    );
    req.flush(DEFAULT_MOCK_DOSSIER);
  });

  afterEach(() => {
    http.verify();
  });

  it('should create the component and load dossier', () => {
    expect(component).toBeTruthy();
    expect(component.viewState()).toBe('success');
    expect(component.dossier()).toBeTruthy();
    expect(component.dossier()?.submissionCode).toBe('#SUB-WRK-2026-089-V1');
  });

  it('should toggle control standards', () => {
    const std03 = component.controlStandards().find((s) => s.id === 'STD-03');
    expect(std03?.passed).toBeFalse();

    component.toggleStandard('STD-03');
    const updatedStd03 = component.controlStandards().find((s) => s.id === 'STD-03');
    expect(updatedStd03?.passed).toBeTrue();
  });

  it('should switch decision type and validate feedback requirement', () => {
    component.selectDecision('APPROVED');
    expect(component.selectedDecision()).toBe('APPROVED');
    expect(component.isFeedbackValid()).toBeTrue();

    component.selectDecision('REVISION_REQUIRED');
    expect(component.selectedDecision()).toBe('REVISION_REQUIRED');

    // Set feedback to short string
    const event = { target: { value: 'short' } } as unknown as Event;
    component.setFeedback(event);
    expect(component.isFeedbackValid()).toBeFalse();
    expect(component.canSubmitDecision()).toBeFalse();

    // Set feedback to valid string >= 10 chars
    const validEvent = {
      target: { value: 'Yêu cầu kiểm tra lại chữ ký hợp đồng' },
    } as unknown as Event;
    component.setFeedback(validEvent);
    expect(component.isFeedbackValid()).toBeTrue();
    expect(component.canSubmitDecision()).toBeTrue();
  });

  it('should prevent submission when maker-checker rule is violated', () => {
    component.switchScenario('maker-checker');
    expect(component.isMakerCheckerViolated()).toBeTrue();
    expect(component.canSubmitDecision()).toBeFalse();

    component.openConfirmModal();
    expect(component.showConfirmModal()).toBeFalse();
    expect(component.validationError()).toBe(component.localization.review.makerCheckerError);
  });

  it('should handle document zoom and page navigation', () => {
    const initialZoom = component.docZoom();
    component.zoomIn();
    expect(component.docZoom()).toBe(initialZoom + 15);

    component.zoomOut();
    expect(component.docZoom()).toBe(initialZoom);

    expect(component.docCurrentPage()).toBe(1);
    component.nextPage();
    expect(component.docCurrentPage()).toBe(2);
    component.prevPage();
    expect(component.docCurrentPage()).toBe(1);
  });

  it('should open modal and submit audit decision successfully', () => {
    component.switchScenario('default');
    expect(component.canSubmitDecision()).toBeTrue();

    component.openConfirmModal();
    expect(component.showConfirmModal()).toBeTrue();

    component.confirmDecision();
    const req = http.expectOne(
      `${environment.apiUrl}/verifications/${DEFAULT_MOCK_DOSSIER.submissionId}/decision`,
    );
    expect(req.request.method).toBe('POST');
    req.flush({
      verificationId: 'ver-12345',
      submissionStatus: 'REVISION_REQUIRED',
      verifiedAt: '2026-10-06T09:15:00Z',
      message: 'Quyết định thẩm định đã được ghi nhận.',
    });

    expect(component.showConfirmModal()).toBeFalse();
    expect(component.decisionResult()).toBeTruthy();
    expect(component.decisionResult()?.submissionStatus).toBe('REVISION_REQUIRED');
  });

  it('should handle simulation scenarios', () => {
    component.switchScenario('loading');
    expect(component.viewState()).toBe('loading');

    component.switchScenario('empty');
    expect(component.viewState()).toBe('empty');

    component.switchScenario('error');
    expect(component.viewState()).toBe('error');

    component.switchScenario('approved');
    expect(component.viewState()).toBe('success');
    expect(component.selectedDecision()).toBe('APPROVED');
  });
});
