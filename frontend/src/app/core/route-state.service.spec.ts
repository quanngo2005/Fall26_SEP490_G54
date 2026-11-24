import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, withRouterConfig } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { RouteStateService, stateValue } from './route-state.service';

/** Records what the service reports while the page is being created, and every later emission. */
@Component({ template: '' })
class DetailPage {
  private readonly routeState = inject(RouteStateService);
  readonly atCreation = this.routeState.value('recordId');
  readonly seen: (string | null)[] = [];

  constructor() {
    this.routeState.watch('recordId').subscribe((id) => this.seen.push(id));
  }
}

describe('RouteStateService', () => {
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(
          [
            { path: 'list', component: DetailPage },
            { path: 'detail', component: DetailPage },
          ],
          withRouterConfig({ onSameUrlNavigation: 'reload' }),
        ),
      ],
    });
    router = TestBed.inject(Router);
  });

  const page = (harness: RouterTestingHarness) =>
    harness.routeDebugElement!.componentInstance as DetailPage;

  it('gives the page the id of the navigation that creates it, while the URL names no record', async () => {
    const harness = await RouterTestingHarness.create();
    await router.navigate(['/detail'], { state: { recordId: 'r1' } });

    expect(router.url).toBe('/detail');
    expect(page(harness).atCreation).toBe('r1');
    expect(page(harness).seen).toEqual(['r1']);
  });

  it('reports another record opened on the same URL, once per change', async () => {
    const harness = await RouterTestingHarness.create();
    await router.navigate(['/detail'], { state: { recordId: 'r1' } });
    const detail = page(harness);

    await router.navigate(['/detail'], { state: { recordId: 'r2' } });
    await router.navigate(['/detail'], { state: { recordId: 'r2' } });

    expect(page(harness)).toBe(detail);
    expect(detail.seen).toEqual(['r1', 'r2']);
    expect(router.url).toBe('/detail');
  });

  it('falls back to the history entry when no navigation is running (a refresh restores it)', () => {
    const service = TestBed.inject(RouteStateService);
    const previous = history.state as unknown;
    history.replaceState({ recordId: 'from-history' }, '');
    try {
      expect(service.value('recordId')).toBe('from-history');
    } finally {
      history.replaceState(previous, '');
    }
  });

  it('reports null when the page is opened without a record', async () => {
    const harness = await RouterTestingHarness.create();
    await router.navigate(['/detail']);

    expect(page(harness).atCreation).toBeNull();
  });

  it('reads only non-empty strings from a state', () => {
    expect(stateValue({ id: 'a' }, 'id')).toBe('a');
    expect(stateValue({ id: '' }, 'id')).toBeNull();
    expect(stateValue({ id: 5 }, 'id')).toBeNull();
    expect(stateValue(null, 'id')).toBeNull();
    expect(stateValue('text', 'id')).toBeNull();
  });
});
