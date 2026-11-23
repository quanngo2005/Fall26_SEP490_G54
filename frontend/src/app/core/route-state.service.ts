import { inject, Injectable } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { distinctUntilChanged, filter, map, Observable, startWith } from 'rxjs';

/**
 * Carries record ids in the navigation's history state instead of the address, so a page's URL names no record.
 * Open a page with `router.navigate(['/x/detail'], { state: { id } })` or `routerLink` + `[state]`.
 * The state survives a refresh and Back/Forward; a copied link or a new tab opens without it.
 */
@Injectable({ providedIn: 'root' })
export class RouteStateService {
  private readonly router = inject(Router);

  /** The non-empty string under `key` in the navigation being activated, or in the current history entry. */
  value(key: string): string | null {
    return stateValue(this.router.currentNavigation()?.extras.state ?? historyState(), key);
  }

  /** `value(key)` now and again after each later navigation, e.g. a jump to a sibling record on the same page. */
  watch(key: string): Observable<string | null> {
    return this.watchState().pipe(
      map((state) => stateValue(state, key)),
      distinctUntilChanged(),
    );
  }

  /** The whole navigation state now and after each later navigation; read keys with `stateValue`. */
  watchState(): Observable<unknown> {
    return this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.lastSuccessfulNavigation()?.extras.state ?? historyState()),
      startWith(this.router.currentNavigation()?.extras.state ?? historyState()),
    );
  }
}

/** The non-empty string under `key` in a navigation state, or null. */
export function stateValue(state: unknown, key: string): string | null {
  if (typeof state !== 'object' || state === null) return null;
  const value = (state as Record<string, unknown>)[key];
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function historyState(): unknown {
  return typeof history === 'undefined' ? null : (history.state as unknown);
}
