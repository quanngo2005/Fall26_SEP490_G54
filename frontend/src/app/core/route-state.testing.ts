import { Provider } from '@angular/core';
import { BehaviorSubject, distinctUntilChanged, map, Observable } from 'rxjs';
import { RouteStateService, stateValue } from './route-state.service';

/** Test double for `RouteStateService`: the navigation state is whatever the spec sets. */
export class FakeRouteState {
  private readonly state$: BehaviorSubject<Record<string, string>>;

  constructor(initial: Record<string, string> = {}) {
    this.state$ = new BehaviorSubject(initial);
  }

  /** Simulates a navigation that carries `state`. */
  set(state: Record<string, string>): void {
    this.state$.next(state);
  }

  value(key: string): string | null {
    return stateValue(this.state$.value, key);
  }

  watch(key: string): Observable<string | null> {
    return this.state$.pipe(
      map((state) => stateValue(state, key)),
      distinctUntilChanged(),
    );
  }

  watchState(): Observable<unknown> {
    return this.state$.asObservable();
  }
}

/** Provides a `FakeRouteState` seeded with `initial`; returns it so the spec can change the state later. */
export function provideRouteState(initial: Record<string, string> = {}): {
  fake: FakeRouteState;
  providers: Provider[];
} {
  const fake = new FakeRouteState(initial);
  return { fake, providers: [{ provide: RouteStateService, useValue: fake }] };
}
