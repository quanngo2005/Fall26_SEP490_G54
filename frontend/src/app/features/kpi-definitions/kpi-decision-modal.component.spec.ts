import { HttpErrorResponse } from '@angular/common/http';
import { ComponentRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { I18nService } from '../../core/i18n/i18n.service';
import { KpiDefinition } from '../../core/kpi-definition.models';
import { KpiDefinitionService } from '../../core/kpi-definition.service';
import { KpiDecision, KpiDecisionModalComponent } from './kpi-decision-modal.component';

const KPI = { id: 'k1', code: 'KPI-CARD-R', name: 'Mở thẻ – giới thiệu' } as KpiDefinition;

describe('KpiDecisionModalComponent', () => {
  const api = jasmine.createSpyObj<KpiDefinitionService>('KpiDefinitionService', [
    'approve',
    'reject',
  ]);
  let fixture: ComponentFixture<KpiDecisionModalComponent>;
  let element: HTMLElement;

  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  async function create(initial: KpiDecision = 'APPROVE'): Promise<void> {
    api.approve.calls.reset();
    api.reject.calls.reset();
    await TestBed.configureTestingModule({
      imports: [KpiDecisionModalComponent],
      providers: [{ provide: KpiDefinitionService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
    fixture = TestBed.createComponent(KpiDecisionModalComponent);
    const ref: ComponentRef<KpiDecisionModalComponent> = fixture.componentRef;
    ref.setInput('kpi', KPI);
    ref.setInput('initial', initial);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const submit = () => {
    element.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
    fixture.detectChanges();
  };

  const decisionSwitch = () => element.querySelector<HTMLButtonElement>('#kpi-decision-switch')!;

  const flip = () => {
    decisionSwitch().click();
    fixture.detectChanges();
  };

  it('names the KPI and starts on the decision it was opened with', async () => {
    await create('REJECT');

    expect(text(element.querySelector('h2'))).toBe('Quyết định KPI-CARD-R');
    expect(text(element.querySelector('.dialog-head p'))).toBe('Mở thẻ – giới thiệu');
    expect(decisionSwitch().getAttribute('role')).toBe('switch');
    expect(decisionSwitch().getAttribute('aria-checked')).toBe('false');
    expect(text(element.querySelector('.side.is-active'))).toBe('Từ chối');
    expect(element.querySelector('input[type="radio"]')).toBeNull();
    expect(text(element.querySelector('label.label'))).toContain('bắt buộc khi từ chối');
  });

  it('approves without a reason', async () => {
    api.approve.and.returnValue(of(undefined));
    await create('APPROVE');
    let result: { decision: KpiDecision; kpi: KpiDefinition } | undefined;
    fixture.componentInstance.decided.subscribe((value) => (result = value));

    submit();

    expect(api.approve).toHaveBeenCalledOnceWith('k1');
    expect(result).toEqual({ decision: 'APPROVE', kpi: KPI });
  });

  it('will not reject without a reason, and rejects with the trimmed reason', async () => {
    api.reject.and.returnValue(of(undefined));
    await create('REJECT');

    submit();
    expect(api.reject).not.toHaveBeenCalled();
    expect(text(element.querySelector('.field-error'))).toBe('Trường * là bắt buộc. (MSG02)');

    const reason = element.querySelector<HTMLTextAreaElement>('#kpi-decision-reason')!;
    reason.value = '  Thiếu công thức  ';
    reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    submit();

    expect(api.reject).toHaveBeenCalledOnceWith('k1', 'Thiếu công thức');
  });

  it('starts on Approve with the switch on, and flips between the two decisions', async () => {
    await create('APPROVE');
    expect(decisionSwitch().getAttribute('aria-checked')).toBe('true');
    expect(text(element.querySelector('.side.is-active'))).toBe('Duyệt');

    flip();
    expect(decisionSwitch().getAttribute('aria-checked')).toBe('false');
    expect(text(element.querySelector('.side.is-active'))).toBe('Từ chối');

    flip();
    expect(decisionSwitch().getAttribute('aria-checked')).toBe('true');
  });

  it('asks for a reason once the switch is moved to Reject', async () => {
    await create('APPROVE');

    flip();
    submit();

    expect(api.approve).not.toHaveBeenCalled();
    expect(element.querySelector('.field-error')).not.toBeNull();
  });

  it('explains why the decision failed and stays open', async () => {
    await create('APPROVE');
    const fail = (status: number, errorCode?: string) => {
      api.approve.and.returnValue(
        throwError(() => new HttpErrorResponse({ status, error: { errorCode } })),
      );
      submit();
      return text(element.querySelector('.form-error'));
    };

    expect(fail(422, 'MAKER_CANNOT_BE_CHECKER')).toContain('không thể tự duyệt');
    expect(fail(409)).toContain('đã thay đổi');
    expect(fail(404)).toContain('đã thay đổi');
    expect(fail(500)).toContain('Không thực hiện được');
  });

  it('closes on Escape', async () => {
    await create();
    let closed = false;
    fixture.componentInstance.closed.subscribe(() => (closed = true));

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(closed).toBeTrue();
  });
});
