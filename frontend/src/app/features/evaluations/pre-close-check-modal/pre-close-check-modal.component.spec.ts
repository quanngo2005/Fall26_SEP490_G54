import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PreCloseCheckModalComponent } from './pre-close-check-modal.component';
import { EvaluationService } from '../../../core/evaluation.service';
import { I18nService } from '../../../core/i18n/i18n.service';
import { Localization } from '../../../core/i18n/i18n.models';
import { PreCloseCheckOverview } from '../../../core/evaluation.models';

describe('PreCloseCheckModalComponent', () => {
  let component: PreCloseCheckModalComponent;
  let fixture: ComponentFixture<PreCloseCheckModalComponent>;
  let evaluationServiceSpy: jasmine.SpyObj<EvaluationService>;
  let i18nServiceSpy: jasmine.SpyObj<I18nService>;

  const mockOverview: PreCloseCheckOverview = {
    periodId: 'test-period-id',
    periodCode: '2026-Q3',
    periodName: 'Quý 3/2026',
    startDate: '2026-07-01',
    endDate: '2026-09-30',
    preCloseDeadline: '2026-10-12T17:00:00Z',
    canPublish: false,
    totalUnresolvedCount: 2,
    items: [
      {
        code: 'IN_REVIEW_SUBMISSIONS',
        title: 'Kết quả In Review (chưa khớp giao dịch import)',
        remainingCount: 3,
        isPassed: false,
        targetScreenCode: 'SCR-REV-03',
        targetRoute: '/results/verify',
      },
      {
        code: 'CORE_BANKING_IMPORT',
        title: 'File import Core Banking đã đủ tới 30/09/2026',
        remainingCount: 0,
        isPassed: true,
      },
    ],
  };

  beforeEach(async () => {
    evaluationServiceSpy = jasmine.createSpyObj('EvaluationService', ['getPreCloseCheck']);
    i18nServiceSpy = jasmine.createSpyObj('I18nService', [], {
      localization: {
        evaluations: {
          preCloseCheck: {
            modalTitle: 'Kiểm tra trước khi đóng {periodName}',
            modalSubtitle: 'SCR-EVL-03-M1 · hạn pre-close {deadline} (tham số SCR-ADM-04)',
            colItem: 'HẠNG MỤC',
            colRemaining: 'CÒN LẠI',
            colResult: 'KẾT QUẢ',
            statusPassed: 'Đạt',
            statusFailed: 'Chưa đạt',
            actionView: 'Xem',
            bannerFailed: 'Còn {count} hạng mục chưa đạt. Chưa publish được.',
            bannerPassed: 'Tất cả các hạng mục đều đạt. Có thể tiến hành publish kết quả.',
            btnClose: 'Đóng',
            btnRecheck: 'Kiểm tra lại',
            btnContinuePublish: 'Tiếp tục publish',
          },
        },
      } as unknown as Localization,
    });

    evaluationServiceSpy.getPreCloseCheck.and.returnValue(of(mockOverview));

    await TestBed.configureTestingModule({
      imports: [PreCloseCheckModalComponent],
      providers: [
        { provide: EvaluationService, useValue: evaluationServiceSpy },
        { provide: I18nService, useValue: i18nServiceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PreCloseCheckModalComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('isOpen', true);
    fixture.componentRef.setInput('periodId', 'test-period-id');
    fixture.detectChanges();
  });

  it('should create the component and fetch checklist data', () => {
    expect(component).toBeTruthy();
    expect(evaluationServiceSpy.getPreCloseCheck).toHaveBeenCalledWith('test-period-id');
    expect(component.overview()).toEqual(mockOverview);
  });

  it('should render checklist rows in table', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    const rows = compiled.querySelectorAll('.checklist-table tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Kết quả In Review');
    expect(rows[0].textContent).toContain('Chưa đạt');
    expect(rows[1].textContent).toContain('File import Core Banking');
    expect(rows[1].textContent).toContain('Đạt');
  });

  it('should disable continue publish button when canPublish is false', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    const continueBtn = compiled.querySelector('.btn-primary') as HTMLButtonElement;
    expect(continueBtn.disabled).toBeTrue();
  });

  it('should enable continue publish button when canPublish is true', () => {
    const passedOverview: PreCloseCheckOverview = {
      ...mockOverview,
      canPublish: true,
      totalUnresolvedCount: 0,
      items: mockOverview.items.map((i) => ({ ...i, isPassed: true, remainingCount: 0 })),
    };
    component.overview.set(passedOverview);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const continueBtn = compiled.querySelector('.btn-primary') as HTMLButtonElement;
    expect(continueBtn.disabled).toBeFalse();
  });

  it('should emit closeModal when close button is clicked', () => {
    spyOn(component.closeModal, 'emit');
    const compiled = fixture.nativeElement as HTMLElement;
    const closeBtn = compiled.querySelector('.btn-secondary') as HTMLButtonElement;
    closeBtn.click();
    expect(component.closeModal.emit).toHaveBeenCalled();
  });

  it('should emit continuePublish when continue button is clicked', () => {
    spyOn(component.continuePublish, 'emit');
    component.overview.set({ ...mockOverview, canPublish: true, totalUnresolvedCount: 0 });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const continueBtn = compiled.querySelector('.btn-primary') as HTMLButtonElement;
    continueBtn.click();
    expect(component.continuePublish.emit).toHaveBeenCalled();
  });

  it('should reload data when onRecheck is called', () => {
    evaluationServiceSpy.getPreCloseCheck.calls.reset();
    component.onRecheck();
    expect(evaluationServiceSpy.getPreCloseCheck).toHaveBeenCalledWith('test-period-id');
  });
});
