import { Component, input, output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { JobPositionSummary } from '../../core/job-position.models';
import { JobPositionService } from '../../core/job-position.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { JobPositionDetailDrawerComponent } from './job-position-detail-drawer.component';
import { JobPositionListComponent } from './job-position-list.component';

@Component({ selector: 'app-header', standalone: true, template: '' })
class HeaderStub {}

@Component({ selector: 'app-sidebar', standalone: true, template: '' })
class SidebarStub {}

@Component({ selector: 'app-job-position-detail-drawer', standalone: true, template: '' })
class DrawerStub {
  readonly positionId = input.required<string>();
  readonly closeDrawer = output<void>();
}

function position(code: string, extra: Partial<JobPositionSummary> = {}): JobPositionSummary {
  return {
    id: `id-${code}`,
    code,
    title: `Title ${code}`,
    description: `About ${code}`,
    employeeCount: 7,
    evaluationProfileId: `ep-${code}`,
    evaluationProfileName: `Profile ${code}`,
    workTypeCount: 3,
    kpiItems: ['KPI A', 'KPI B'],
    lastSyncAt: '2026-10-24T08:15:00+07:00',
    status: 'ACTIVE',
    ...extra,
  };
}

type Page = { items: JobPositionSummary[]; totalCount: number; page: number; pageSize: number };

describe('JobPositionListComponent', () => {
  let service: jasmine.SpyObj<JobPositionService>;
  let i18n: I18nService;
  const items = [
    position('SALES'),
    position('TELLER', {
      description: null,
      evaluationProfileId: null,
      evaluationProfileName: null,
      employeeCount: 0,
      workTypeCount: 0,
      kpiItems: [],
    }),
  ];

  const page = (list: JobPositionSummary[], totalCount = list.length): Page => ({
    items: list,
    totalCount,
    page: 1,
    pageSize: 50,
  });
  const text = (el: Element | null | undefined) =>
    (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const rows = (fixture: ComponentFixture<unknown>) =>
    Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.position-row'),
    );

  async function create(): Promise<ComponentFixture<JobPositionListComponent>> {
    const fixture = TestBed.createComponent(JobPositionListComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(async () => {
    service = jasmine.createSpyObj<JobPositionService>('JobPositionService', ['list']);
    service.list.and.returnValue(of(page(items)));
    await TestBed.configureTestingModule({
      imports: [JobPositionListComponent],
      providers: [{ provide: JobPositionService, useValue: service }],
    })
      .overrideComponent(JobPositionListComponent, {
        remove: { imports: [HeaderComponent, SidebarComponent, JobPositionDetailDrawerComponent] },
        add: { imports: [HeaderStub, SidebarStub, DrawerStub] },
      })
      .compileComponents();
    i18n = TestBed.inject(I18nService);
    i18n.setLanguage('vi');
  });

  it('calls the API for the first page and renders each row from the response only', async () => {
    const fixture = await create();

    expect(service.list).toHaveBeenCalledOnceWith({
      keyword: undefined,
      status: undefined,
      page: 1,
      pageSize: 50,
    });
    const [sales] = rows(fixture);
    expect(text(sales.querySelector('.col-code'))).toBe('SALES'); // the code is shown untouched
    expect(text(sales.querySelector('.col-title'))).toBe('Title SALES');
    expect(text(sales.querySelector('.col-desc'))).toBe('About SALES');
    expect(text(sales.querySelector('.col-profile'))).toBe('Profile SALES');
    expect(text(sales.querySelector('.work-type-num'))).toBe('3');
    expect(Array.from(sales.querySelectorAll('.kpi-pill')).map((k) => text(k))).toEqual([
      'KPI A',
      'KPI B',
    ]);
  });

  it('shows N/A for missing data and a real zero for zero counts, nothing invented', async () => {
    const fixture = await create();
    const teller = rows(fixture)[1];

    expect(text(teller.querySelector('.col-desc'))).toBe('N/A');
    expect(text(teller.querySelector('.col-profile'))).toBe('N/A');
    expect(text(teller.querySelectorAll('.col-num')[0])).toBe('0');
    expect(text(teller.querySelector('.work-type-num'))).toBe('0');
    expect(teller.querySelectorAll('.kpi-pill').length).toBe(0);
    expect(text(teller)).not.toMatch(/Q4\/2026|Giới thiệu|Xử lý/);
  });

  it('builds the sync banner from the data, with total and latest sync, and no fixed date', async () => {
    const fixture = await create();
    const banner = text((fixture.nativeElement as HTMLElement).querySelector('.sync-banner'));

    expect(banner).toMatch(/^Hệ thống có 2 vị trí\./);
    expect(banner).toMatch(/đồng bộ lần cuối 2\d\/10\/2026 \d{2}:\d{2}$/);

    service.list.and.returnValue(of(page([])));
    const empty = await create();
    const emptyBanner = text((empty.nativeElement as HTMLElement).querySelector('.sync-banner'));
    expect(emptyBanner).toContain('Hệ thống có 0 vị trí');
    expect(emptyBanner).toContain('N/A');
    expect(emptyBanner).not.toContain('24/10/2026');
  });

  it('uses the latest sync time when rows were synchronised at different times', async () => {
    service.list.and.returnValue(
      of(
        page([
          position('A', { lastSyncAt: '2026-10-01T00:00:00+00:00' }),
          position('B', { lastSyncAt: 'not a date' }),
          position('C', { lastSyncAt: '2026-10-20T10:00:00+00:00' }),
        ]),
      ),
    );
    const fixture = await create();

    const latest = (
      fixture.componentInstance as unknown as { lastSyncAt(): string | null }
    ).lastSyncAt();
    expect(latest).toBe('2026-10-20T10:00:00.000Z');
  });

  it('shows the empty state for a real empty response', async () => {
    service.list.and.returnValue(of(page([])));
    const fixture = await create();

    expect(rows(fixture).length).toBe(0);
    expect(text((fixture.nativeElement as HTMLElement).querySelector('.empty-cell'))).toBe(
      'Không có dữ liệu',
    );
  });

  it('shows the error state with a working retry and no sample rows', async () => {
    service.list.and.returnValues(
      throwError(() => new Error('500')),
      of(page(items)),
    );
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[role="alert"]')).not.toBeNull();
    expect(rows(fixture).length).toBe(0);
    expect(text(root.querySelector('[role="alert"] button'))).toBe('Thử lại');

    root.querySelector<HTMLButtonElement>('[role="alert"] button')?.click();
    fixture.detectChanges();

    expect(service.list).toHaveBeenCalledTimes(2);
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(rows(fixture).length).toBe(2);
  });

  it('shows the loading state before the first response arrives', () => {
    service.list.and.returnValue(new Subject<Page>());
    const fixture = TestBed.createComponent(JobPositionListComponent);
    fixture.detectChanges();

    expect(text((fixture.nativeElement as HTMLElement).querySelector('.state-card'))).toBe(
      'Đang tải dữ liệu...',
    );
    expect((fixture.nativeElement as HTMLElement).getAttribute('aria-busy')).toBeNull();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('main')?.getAttribute('aria-busy'),
    ).toBe('true');
  });

  it('ignores a slow stale response once a newer request was made', async () => {
    const slow = new Subject<Page>();
    service.list.and.returnValues(slow, of(page([position('NEW')])));
    const fixture = await create();
    (fixture.componentInstance as unknown as { refresh(): void }).refresh();
    fixture.detectChanges();
    slow.next(page([position('OLD')]));
    fixture.detectChanges();

    expect(rows(fixture).map((row) => text(row.querySelector('.col-code')))).toEqual(['NEW']);
  });

  it('opens the drawer on click, toggles it closed on a second click, and supports the keyboard', async () => {
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('app-job-position-detail-drawer')).toBeNull();
    expect(root.querySelector('.callout-card')).not.toBeNull();

    rows(fixture)[0].click();
    fixture.detectChanges();
    expect(rows(fixture)[0].classList).toContain('is-selected');
    expect(rows(fixture)[0].getAttribute('aria-selected')).toBe('true');
    expect(root.querySelector('app-job-position-detail-drawer')).not.toBeNull();
    expect(root.querySelector('.callout-card')).toBeNull();

    rows(fixture)[0].click();
    fixture.detectChanges();
    expect(root.querySelector('app-job-position-detail-drawer')).toBeNull();

    rows(fixture)[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();
    expect(rows(fixture)[1].classList).toContain('is-selected');

    const space = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true });
    rows(fixture)[0].dispatchEvent(space);
    fixture.detectChanges();
    expect(space.defaultPrevented).toBeTrue();
    expect(rows(fixture)[0].classList).toContain('is-selected');
  });

  it('closes the drawer on Escape and from the drawer close output', async () => {
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;
    rows(fixture)[0].click();
    fixture.detectChanges();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(root.querySelector('app-job-position-detail-drawer')).toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })); // nothing open: no-op
    rows(fixture)[0].click();
    fixture.detectChanges();
    (fixture.componentInstance as unknown as { closeDrawer(): void }).closeDrawer();
    fixture.detectChanges();
    expect(root.querySelector('app-job-position-detail-drawer')).toBeNull();
  });

  it('drops a selection that is no longer in the reloaded result', async () => {
    const fixture = await create();
    rows(fixture)[0].click();
    fixture.detectChanges();

    service.list.and.returnValue(of(page([position('OTHER')])));
    (fixture.componentInstance as unknown as { refresh(): void }).refresh();
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('app-job-position-detail-drawer'),
    ).toBeNull();
  });

  it('sends the trimmed keyword and status and starts again from page 1', async () => {
    const fixture = await create();
    const component = fixture.componentInstance as unknown as {
      keyword: string;
      status: string;
      page: number;
      refresh(): void;
    };

    component.keyword = '  teller ';
    component.status = 'INACTIVE';
    component.page = 4;
    component.refresh();

    expect(service.list.calls.mostRecent().args[0]).toEqual({
      keyword: 'teller',
      status: 'INACTIVE',
      page: 1,
      pageSize: 50,
    });
  });

  it('switches labels and the sync banner language live', async () => {
    const fixture = await create();
    const root = fixture.nativeElement as HTMLElement;
    expect(text(root.querySelector('h1'))).toBe('Vị trí công việc');
    expect(text(root.querySelector('.sync-banner'))).toContain('Hệ thống có 2 vị trí');

    i18n.setLanguage('en');
    fixture.detectChanges();

    expect(text(root.querySelector('h1'))).toBe('Job Positions');
    expect(text(root.querySelector('.sync-banner'))).toContain('The system has 2 positions');
    expect(text(root.querySelector('.sync-banner'))).toContain('last synchronized');
    expect(
      text(root.querySelectorAll('.job-position-table tbody tr')[1].querySelector('.col-desc')),
    ).toBe('N/A');
  });
});
