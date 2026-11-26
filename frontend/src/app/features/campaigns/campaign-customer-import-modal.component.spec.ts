import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CustomerImportPreview } from '../../core/campaign-customer.models';
import { CampaignCustomerService } from '../../core/campaign-customer.service';
import { CampaignDetail } from '../../core/campaign.models';
import { I18nService } from '../../core/i18n/i18n.service';
import { CampaignCustomerImportModalComponent } from './campaign-customer-import-modal.component';

const campaign = { id: 'c1', code: 'CMP-2026-06' } as unknown as CampaignDetail;

const preview = (invalid: number, valid: number): CustomerImportPreview => ({
  fileName: 'khach-hang.xlsx',
  summary: { totalRows: valid + invalid, valid, invalid, create: valid, update: 0 },
  rows: [
    {
      rowNumber: 2,
      action: 'CREATE',
      data: {
        'Customer CIF*': '0012345',
        'Tên khách hàng*': 'Nguyễn Văn B',
        'Số điện thoại': '0901234567',
      },
      errors: [],
    },
    {
      rowNumber: 15,
      action: 'INVALID',
      data: { 'Tên khách hàng*': 'Lê Văn F' },
      errors: [{ column: 'Customer CIF*', code: 'DATA_IMPORT_REQUIRED', message: 'bắt buộc' }],
    },
  ],
});

describe('CampaignCustomerImportModalComponent', () => {
  const api = jasmine.createSpyObj<CampaignCustomerService>('CampaignCustomerService', [
    'importPreview',
    'importCommit',
    'downloadTemplate',
  ]);

  const open = async () => {
    const fixture = TestBed.createComponent(CampaignCustomerImportModalComponent);
    fixture.componentRef.setInput('campaign', campaign);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  };

  const chooseFile = (root: HTMLElement, name: string, size = 10) => {
    const input = root.querySelector<HTMLInputElement>('input[type="file"]')!;
    const file = new File([new Uint8Array(size)], name);
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
  };

  const primary = (root: HTMLElement) =>
    root.querySelector<HTMLButtonElement>('.modal-footer .button-primary')!;

  beforeEach(async () => {
    api.importPreview.calls.reset();
    api.importCommit.calls.reset();
    api.importPreview.and.returnValue(of(preview(1, 1)));
    api.importCommit.and.returnValue(of({ created: 1, updated: 0, skipped: 1 }));
    await TestBed.configureTestingModule({
      imports: [CampaignCustomerImportModalComponent],
      providers: [{ provide: CampaignCustomerService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('vi');
  });

  it('rejects a file that is not .xlsx', async () => {
    const fixture = await open();
    const root: HTMLElement = fixture.nativeElement;

    chooseFile(root, 'khach-hang.csv');
    fixture.detectChanges();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('.xlsx');
    expect(primary(root).disabled).toBe(true);
  });

  it('previews the rows and points every error at its row and column', async () => {
    const fixture = await open();
    const root: HTMLElement = fixture.nativeElement;

    chooseFile(root, 'khach-hang.xlsx');
    fixture.detectChanges();
    primary(root).click();
    fixture.detectChanges();

    expect(api.importPreview).toHaveBeenCalled();
    expect(root.querySelector('.row-invalid')?.textContent).toContain(
      'Dòng 15, cột Customer CIF*: bắt buộc',
    );
    expect(root.querySelector('.check-row')?.textContent).toContain('Bỏ qua 1 dòng lỗi');
  });

  it('only commits error rows when the skip box is ticked', async () => {
    const fixture = await open();
    const root: HTMLElement = fixture.nativeElement;
    chooseFile(root, 'khach-hang.xlsx');
    fixture.detectChanges();
    primary(root).click();
    fixture.detectChanges();

    root.querySelector<HTMLInputElement>('.check-row input')!.click();
    fixture.detectChanges();
    expect(primary(root).disabled).toBe(true);

    root.querySelector<HTMLInputElement>('.check-row input')!.click();
    fixture.detectChanges();
    primary(root).click();
    fixture.detectChanges();

    expect(api.importCommit).toHaveBeenCalled();
    expect(root.querySelector('.notice')?.textContent).toContain(
      'Đã tạo 1, cập nhật 0, bỏ qua 1 dòng lỗi',
    );
  });

  it('cannot commit a file with no valid rows', async () => {
    api.importPreview.and.returnValue(of(preview(2, 0)));
    const fixture = await open();
    const root: HTMLElement = fixture.nativeElement;
    chooseFile(root, 'khach-hang.xlsx');
    fixture.detectChanges();
    primary(root).click();
    fixture.detectChanges();

    expect(primary(root).disabled).toBe(true);
    expect(root.textContent).toContain('không có dòng hợp lệ');
  });
});
