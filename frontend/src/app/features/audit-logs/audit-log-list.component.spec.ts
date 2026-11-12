import { signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
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
  let queryParams: Record<string, string>;

  function create() {
    TestBed.inject(I18nService).setLanguage('vi');
    const fixture = TestBed.createComponent(AuditLogListComponent);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    localStorage.clear();
    queryParams = {};
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
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              get queryParamMap() {
                return convertToParamMap(queryParams);
              },
            },
          },
        },
      ],
    });
  });
  afterEach(() => localStorage.clear());

  it('GB654_18 loads the newest page first with the default sort and renders one row per record', () => {
    const fixture = create();
    const t = TestBed.inject(I18nService).localization.auditLog;
    const component = fixture.componentInstance;
    expect(list).toHaveBeenCalledWith(
      jasmine.objectContaining({ page: 1, pageSize: 20, sort: 'occurredAt desc' }),
    );
    const rows = fixture.nativeElement.querySelectorAll('.log-table tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].querySelector('.entity')?.textContent).toBe(item.entityName);
    expect(rows[0].textContent).toContain(item.actorName);
    expect(rows[0].textContent).toContain(component.time(item.occurredAt));
    expect(rows[0].querySelector('.tag')?.getAttribute('data-action')).toBe('Update');
    expect(rows[0].querySelector('.tag')?.textContent).toContain(t.actions.Update);
    expect(fixture.nativeElement.textContent).toContain(
      TestBed.inject(I18nService).translate('auditLog.totalCount', { count: 1 }),
    );
    fixture.destroy();
  });

  it('GB654_18 seeds the filter bar and the request from the entityName query param, e.g. the system parameters audit link', () => {
    queryParams = { entityName: 'SystemParameter' };
    const fixture = create();
    expect(list).toHaveBeenCalledWith(jasmine.objectContaining({ entityName: 'SystemParameter' }));
    expect(fixture.componentInstance.entityName()).toBe('SystemParameter');
    fixture.destroy();
  });

  it('GB654_19 sends filters on Apply, resets to page one, and blocks an inverted date range', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    list.calls.reset();
    component.keyword.set(' admin ');
    component.action.set('Delete');
    component.fromDate.set('2026-11-02');
    component.toDate.set('2026-11-01');
    fixture.detectChanges();
    expect(component.rangeInvalid()).toBeTrue();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBeTrue();
    component.applyFilters();
    expect(list).not.toHaveBeenCalled();

    component.toDate.set('2026-11-03');
    fixture.detectChanges();
    component.applyFilters();
    const query = list.calls.mostRecent().args[0];
    expect(query.keyword).toBe('admin');
    expect(query.action).toBe('Delete');
    expect(query.page).toBe(1);
    expect(new Date(query.from).getTime()).toBeLessThan(new Date(query.to).getTime());
    fixture.destroy();
  });

  it('GB654_19 resets and reloads on Clear', () => {
    const fixture = create();
    const component = fixture.componentInstance;
    component.keyword.set('admin');
    component.action.set('Delete');
    component.applyFilters();
    fixture.detectChanges();
    list.calls.reset();
    const root: HTMLElement = fixture.nativeElement;
    const clearButton = Array.from(
      root.querySelectorAll<HTMLButtonElement>('.list-filters button'),
    ).find((button) => button.type === 'button');
    clearButton?.click();
    fixture.detectChanges();
    expect(component.keyword()).toBe('');
    expect(component.action()).toBe('');
    expect(list).toHaveBeenCalledWith(jasmine.objectContaining({ page: 1, keyword: '' }));
    fixture.destroy();
  });

  it('GB654_19 pages through results, disabling Prev on the first page and Next on the last', () => {
    list.and.returnValue(of(page([item], 45)));
    const fixture = create();
    const component = fixture.componentInstance;
    fixture.detectChanges();
    expect(component.pageCount()).toBe(3);
    const pager = fixture.nativeElement.querySelector('.pager');
    const [prev, next] = pager.querySelectorAll('button');
    expect(prev.disabled).toBeTrue();
    expect(next.disabled).toBeFalse();
    next.click();
    fixture.detectChanges();
    expect(list.calls.mostRecent().args[0].page).toBe(2);
    component.goToPage(3);
    fixture.detectChanges();
    expect(next.disabled).toBeTrue();
    component.goToPage(0);
    component.goToPage(4);
    expect(list).toHaveBeenCalledTimes(3);
    fixture.destroy();
  });

  it('GB654_19 opens the details drawer with request info and an old/new diff, and closes on Escape', () => {
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

  it('GB654_20 shows an error with retry when loading fails, and an empty state when there are no records', () => {
    list.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const fixture = create();
    const t = TestBed.inject(I18nService).localization.auditLog;
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      t.loadFailed,
    );
    expect(fixture.nativeElement.querySelector('.log-table')).toBeNull();
    list.and.returnValue(of(page([])));
    fixture.componentInstance.load();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(t.empty);
    fixture.destroy();
  });

  it('GB654_20 handles a 403 FORBIDDEN list response as an error state instead of swallowing it', () => {
    list.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 403,
            error: { success: false, errorCode: 'FORBIDDEN', message: 'Forbidden' },
          }),
      ),
    );
    const fixture = create();
    const component = fixture.componentInstance;
    const t = TestBed.inject(I18nService).localization.auditLog;
    expect(component.failed()).toBeTrue();
    expect(component.items()).toEqual([]);
    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert).not.toBeNull();
    expect(alert.textContent).toContain(t.loadFailed);
    expect(fixture.nativeElement.querySelector('.log-table')).toBeNull();
    fixture.destroy();
  });

  it('GB654_20 reports a missing record and a failed detail load separately', () => {
    get.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const fixture = create();
    const component = fixture.componentInstance;
    const t = TestBed.inject(I18nService).localization.auditLog;
    component.open(item);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.drawer').textContent).toContain(t.detail.notFound);
    get.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.open(item);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.drawer').textContent).toContain(
      t.detail.loadFailed,
    );
    fixture.destroy();
  });

  it('GB654_21 switches headers, labels and the action badge text to English without reloading', () => {
    const fixture = create();
    list.calls.reset();
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('en');
    fixture.detectChanges();
    const t = i18n.localization.auditLog;
    const root: HTMLElement = fixture.nativeElement;
    expect(list).not.toHaveBeenCalled();
    expect(root.querySelector('h1')?.textContent).toContain(t.title);
    expect(root.querySelector('thead')?.textContent).toContain(t.colTime);
    expect(root.querySelector('.log-table tbody tr')?.textContent).toContain(t.actions.Update);
    expect(root.textContent).not.toContain('auditLog.');
    fixture.destroy();
  });
});
