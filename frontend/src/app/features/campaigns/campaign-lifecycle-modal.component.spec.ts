import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { CampaignDetail } from '../../core/campaign.models';
import { CampaignService } from '../../core/campaign.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { CampaignLifecycleModalComponent } from './campaign-lifecycle-modal.component';

const campaign = {
  id: 'c1',
  code: 'CMP-2026-06',
  metrics: { customerTotal: 48, customerClosed: 9 },
} as unknown as CampaignDetail;

describe('CampaignLifecycleModalComponent', () => {
  const api = jasmine.createSpyObj<CampaignService>('CampaignService', ['complete', 'cancel']);

  const open = async (mode: 'complete' | 'cancel') => {
    const fixture = TestBed.createComponent(CampaignLifecycleModalComponent);
    fixture.componentRef.setInput('campaign', campaign);
    fixture.componentRef.setInput('mode', mode);
    const done: string[] = [];
    fixture.componentInstance.done.subscribe((value) => done.push(value));
    fixture.detectChanges();
    await fixture.whenStable();
    return { fixture, root: fixture.nativeElement as HTMLElement, done };
  };

  const confirm = (root: HTMLElement) =>
    root.querySelector<HTMLButtonElement>('.modal-footer .button-primary')!.click();

  beforeEach(async () => {
    api.complete.calls.reset();
    api.cancel.calls.reset();
    api.complete.and.returnValue(
      of({ campaignId: 'c1', status: 'COMPLETED', decidedAt: '', message: '' }),
    );
    api.cancel.and.returnValue(
      of({ campaignId: 'c1', status: 'COMPLETED', decidedAt: '', message: '' }),
    );
    await TestBed.configureTestingModule({
      imports: [CampaignLifecycleModalComponent],
      providers: [{ provide: CampaignService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('says how many customers are still open before completing', async () => {
    const { fixture, root, done } = await open('complete');

    expect(root.textContent).toContain('Còn 39 khách chưa chốt');
    confirm(root);
    fixture.detectChanges();

    expect(api.complete).toHaveBeenCalledWith('c1');
    expect(done).toEqual(['complete']);
  });

  it('needs a reason to cancel', async () => {
    const { fixture, root, done } = await open('cancel');

    confirm(root);
    fixture.detectChanges();
    expect(api.cancel).not.toHaveBeenCalled();
    expect(root.querySelector('.field-error')?.textContent).toContain('lý do');

    const reason = root.querySelector<HTMLTextAreaElement>('textarea')!;
    reason.value = 'Hết ngân sách';
    reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    confirm(root);
    fixture.detectChanges();

    expect(api.cancel).toHaveBeenCalledWith('c1', 'Hết ngân sách');
    expect(done).toEqual(['cancel']);
  });

  it('shows CAMPAIGN_NOT_COMPLETABLE without closing', async () => {
    api.complete.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { success: false, errorCode: 'CAMPAIGN_NOT_COMPLETABLE' },
          }),
      ),
    );
    const { fixture, root, done } = await open('complete');

    confirm(root);
    fixture.detectChanges();

    expect(root.querySelector('.notice.is-error')?.textContent).toContain('không thể hoàn thành');
    expect(done).toEqual([]);
  });
});
