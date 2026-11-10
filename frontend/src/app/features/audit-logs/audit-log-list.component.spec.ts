import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuditLogDetail, AuditLogListItem } from '../../core/audit-log.models';
import { AuditLogService } from '../../core/audit-log.service';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { PagedResult } from '../../core/paged-result.model';
import { AuditLogListComponent } from './audit-log-list.component';

const item: AuditLogListItem = {
  id: 'log-1',
  entityName: 'RiskEvent',
  entityId: '6b2b6a4e',
  action: 'Update',
  actorId: 'acc-1',
  actorName: 'sa.admin',
  ipAddress: '127.0.0.1',
  occurredAt: '2026-11-05T03:00:00+00:00',
  changedColumns: ['status'],
};
const detail: AuditLogDetail = {
  ...item,
  userAgent: 'curl/8.0',
  httpMethod: 'PUT',
  requestPath: '/risk-events/6b2b6a4e',
  traceId: '0HN-TRACE',
  oldValues: { status: 'OPEN' },
  newValues: { status: 'CONFIRMED' },
};
const page = (
  items: AuditLogListItem[],
  totalCount = items.length,
): PagedResult<AuditLogListItem> => ({
  items,
  totalCount,
  page: 1,
  pageSize: 20,
});

describe('AuditLogListComponent', () => {
  let list: jasmine.Spy;
  let get: jasmine.Spy;

  function create() {
    TestBed.inject(I18nService).setLanguage('vi');
    const fixture = TestBed.createComponent(AuditLogListComponent);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    localStorage.clear();
    list = jasmine.createSpy('list').and.returnValue(of(page([item])));
    get = jasmine.createSpy('get').and.returnValue(of(detail));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser: signal({ id: 'a', fullName: 'Admin', roles: ['ADMIN'], permissions: [] }),
          },
        },
        { provide: AuditLogService, useValue: { list, get } },
      ],
    });
  });
  afterEach(() => localStorage.clear());

  it('loads the newest page first and renders one row per record', () => {
    const fixture = create();
    expect(list).toHaveBeenCalledWith(
      jasmine.objectContaining({ page: 1, pageSize: 20, sort: 'occurredAt desc' }),
    );
    const rows = fixture.nativeElement.querySelectorAll('.log-table tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('RiskEvent');
    expect(rows[0].textContent).toContain('sa.admin');
    expect(rows[0].textContent).toContain('Cập nhật');
    expect(fixture.nativeElement.textContent).toContain('1 bản ghi');
    fixture.destroy();
  });

  it('sends filters, resets to page one and blocks an inverted date range', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    list.calls.reset();
    component.keyword.set(' admin ');
    component.action.set('Delete');
    component.fromDate.set('2026-11-02');
    component.toDate.set('2026-11-01');
    expect(component.rangeInvalid()).toBeTrue();
    component.applyFilters();
    expect(list).not.toHaveBeenCalled();

    component.toDate.set('2026-11-03');
    component.applyFilters();
    const query = list.calls.mostRecent().args[0];
    expect(query.keyword).toBe('admin');
    expect(query.action).toBe('Delete');
    expect(query.page).toBe(1);
    expect(new Date(query.from).getTime()).toBeLessThan(new Date(query.to).getTime());
    fixture.destroy();
  });

  it('pages through results without leaving the valid range', () => {
    list.and.returnValue(of(page([item], 45)));
    const fixture = create();
    const component = fixture.componentInstance;
    expect(component.pageCount()).toBe(3);
    component.goToPage(0);
    component.goToPage(4);
    expect(list).toHaveBeenCalledTimes(1);
    component.goToPage(2);
    expect(list.calls.mostRecent().args[0].page).toBe(2);
    fixture.destroy();
  });

  it('shows an error with retry when loading fails instead of an empty table', () => {
    list.and.returnValue(throwError(() => new Error('down')));
    const fixture = create();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Không thể tải nhật ký audit',
    );
    expect(fixture.nativeElement.querySelector('.log-table')).toBeNull();
    list.and.returnValue(of(page([])));
    fixture.componentInstance.load();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Không có bản ghi audit phù hợp');
    fixture.destroy();
  });

  it('opens the details drawer with request info and an old/new diff, and closes on Escape', () => {
    const fixture = create();
    fixture.nativeElement.querySelector('.log-table tbody tr').click();
    fixture.detectChanges();
    expect(get).toHaveBeenCalledWith('log-1');
    const drawer: HTMLElement = fixture.nativeElement.querySelector('.drawer');
    expect(drawer.textContent).toContain('0HN-TRACE');
    expect(drawer.textContent).toContain('/risk-events/6b2b6a4e');
    expect(drawer.querySelector('.value.old')?.textContent).toContain('OPEN');
    expect(drawer.querySelector('.value.new')?.textContent).toContain('CONFIRMED');
    fixture.componentInstance.close();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.drawer')).toBeNull();
    fixture.destroy();
  });

  it('reports a missing record and a failed detail load separately', () => {
    get.and.returnValue(throwError(() => ({ status: 404 })));
    const fixture = create();
    const component = fixture.componentInstance;
    component.open(item);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.drawer').textContent).toContain(
      'không còn tồn tại',
    );
    get.and.returnValue(throwError(() => ({ status: 500 })));
    component.open(item);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.drawer').textContent).toContain(
      'Không thể tải chi tiết',
    );
    fixture.destroy();
  });

  it('switches to English without reloading', () => {
    const fixture = create();
    TestBed.inject(I18nService).setLanguage('en');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Audit log');
    expect(fixture.nativeElement.querySelector('.log-table tbody tr').textContent).toContain(
      'Update',
    );
    fixture.destroy();
  });
});
