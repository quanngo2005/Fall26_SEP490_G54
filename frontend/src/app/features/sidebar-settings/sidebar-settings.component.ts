import { HttpErrorResponse } from '@angular/common/http';
import {
  CdkDrag,
  CdkDragDrop,
  CdkDragHandle,
  CdkDropList,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { Component, computed, inject, signal } from '@angular/core';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  SidebarConfiguration,
  SidebarConfigurationService,
} from '../../core/sidebar-configuration.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { LayoutIconComponent } from '../../shared/layout/layout-icon.component';
import {
  MenuKey,
  NavigationService,
  ROLE_MENUS,
  WorkspaceRole,
} from '../../shared/layout/navigation.service';

@Component({
  selector: 'app-sidebar-settings',
  imports: [
    HeaderComponent,
    SidebarComponent,
    LayoutIconComponent,
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
  ],
  templateUrl: './sidebar-settings.component.html',
  styleUrls: ['./sidebar-settings.component.scss', './sidebar-settings.component.part2.scss'],
})
export class SidebarSettingsComponent extends LocalizedComponent {
  readonly navigation = inject(NavigationService);
  private readonly api = inject(SidebarConfigurationService);
  readonly selectedRole = signal<WorkspaceRole>('employee');
  readonly configurations = signal<SidebarConfiguration[]>([]);
  readonly draft = signal<MenuKey[]>([]);
  readonly draftRoutes = signal<Partial<Record<MenuKey, string>>>({});
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<'loadFailed' | 'saveFailed' | 'conflict' | null>(null);
  readonly saved = signal(false);
  readonly dirty = computed(
    () =>
      JSON.stringify(this.draft()) !== JSON.stringify(this.current()?.itemIds ?? []) ||
      JSON.stringify(this.draftRoutes()) !== JSON.stringify(this.current()?.routes ?? {}),
  );
  readonly preview = computed(() => this.navigation.buildItems(this.draft(), this.draftRoutes()));
  readonly available = computed(() =>
    this.navigation.catalog.flatMap((item) => [item, ...(item.children ?? [])]),
  );

  constructor() {
    super();
    this.load();
  }

  private current() {
    return this.configurations().find((config) => config.role === this.selectedRole());
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.all().subscribe({
      next: (configs) => {
        this.configurations.set(configs);
        this.draft.set([...(this.current()?.itemIds ?? [])]);
        this.draftRoutes.set({ ...(this.current()?.routes ?? {}) });
        this.loading.set(false);
      },
      error: () => {
        this.error.set('loadFailed');
        this.loading.set(false);
      },
    });
  }

  selectRole(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const role = this.navigation.workspaceRoles.find((role) => role === value);
    if (!role) return;
    if (this.dirty() && !window.confirm(this.localization.sidebarSettings.discardConfirm)) {
      (event.target as HTMLSelectElement).value = this.selectedRole();
      return;
    }
    this.selectedRole.set(role);
    this.draft.set([...(this.current()?.itemIds ?? [])]);
    this.draftRoutes.set({ ...(this.current()?.routes ?? {}) });
    this.error.set(null);
    this.saved.set(false);
  }

  required(id: MenuKey): boolean {
    return id === 'overview' || (id === 'systemConfiguration' && this.selectedRole() === 'admin');
  }

  toggle(id: MenuKey): void {
    if (this.required(id)) return;
    this.saved.set(false);
    this.error.set(null);
    this.draft.update((ids) => {
      if (ids.includes(id)) {
        const children = this.navigation.catalog.find((item) => item.id === id)?.children ?? [];
        return ids.filter((key) => key !== id && !children.some((child) => child.id === key));
      }
      const parent = this.navigation.catalog.find((item) =>
        item.children?.some((child) => child.id === id),
      );
      return [...ids, ...(parent && !ids.includes(parent.id) ? [parent.id] : []), id];
    });
    this.draftRoutes.update((routes) =>
      Object.fromEntries(
        Object.entries(routes).filter(([key]) => this.draft().some((id) => id === key)),
      ),
    );
  }

  move(id: MenuKey, direction: number): void {
    this.draft.update((ids) => {
      const index = ids.indexOf(id);
      const target = index + direction;
      if (target < 0 || target >= ids.length) return ids;
      const next = [...ids];
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
    this.saved.set(false);
  }

  drop(event: CdkDragDrop<MenuKey[]>): void {
    if (this.saving()) return;
    this.draft.update((ids) => {
      const next = [...ids];
      moveItemInArray(next, event.previousIndex, event.currentIndex);
      return next;
    });
    this.saved.set(false);
  }

  routeFor(id: MenuKey): string {
    return this.draftRoutes()[id] ?? this.available().find((item) => item.id === id)?.route ?? '';
  }

  setRoute(id: MenuKey, event: Event): void {
    const input = event.target as HTMLSelectElement;
    const route = input.value.trim();
    if (id === 'organization' || (id === 'systemConfiguration' && this.selectedRole() === 'admin'))
      return;
    if (
      (route && !this.navigation.routeOptions.includes(route)) ||
      (route === '/sidebar-settings' && this.selectedRole() !== 'admin')
    ) {
      input.value = this.routeFor(id);
      return;
    }
    input.value = route;
    this.draftRoutes.update((routes) => ({ ...routes, [id]: route }));
    this.saved.set(false);
  }

  restoreDefaults(): void {
    this.draftRoutes.set({});
    const defaults = ROLE_MENUS[this.selectedRole()];
    this.draft.set(
      defaults.flatMap((id) => [
        id,
        ...(this.navigation.catalog
          .find((item) => item.id === id)
          ?.children?.map((child) => child.id) ?? []),
      ]),
    );
    this.saved.set(false);
  }

  cancel(): void {
    this.draft.set([...(this.current()?.itemIds ?? [])]);
    this.draftRoutes.set({ ...(this.current()?.routes ?? {}) });
    this.error.set(null);
    this.saved.set(false);
  }

  save(): void {
    const current = this.current();
    if (!current || this.saving() || !this.dirty()) return;
    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);
    this.api.save({ ...current, itemIds: this.draft(), routes: this.draftRoutes() }).subscribe({
      next: (config) => {
        this.configurations.update((configs) =>
          configs.map((candidate) => (candidate.role === config.role ? config : candidate)),
        );
        this.draft.set([...config.itemIds]);
        this.draftRoutes.set({ ...(config.routes ?? {}) });
        this.saved.set(true);
        this.saving.set(false);
        this.navigation.reload();
      },
      error: (error: HttpErrorResponse) => {
        this.error.set(error.status === 409 ? 'conflict' : 'saveFailed');
        this.saving.set(false);
      },
    });
  }
}
