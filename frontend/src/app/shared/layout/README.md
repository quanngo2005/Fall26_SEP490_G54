# Shared workspace header and sidebar

Import the standalone components in any page:

```ts
import { HeaderComponent, SidebarComponent } from '../../shared/layout';

@Component({ imports: [HeaderComponent, SidebarComponent] /* ... */ })
```

```html
<div class="workspace-layout">
  <app-sidebar />
  <div class="workspace-main">
    <app-header />
    <main class="workspace-content">Page content</main>
  </div>
</div>
```

Use `display: flex; min-height: 100dvh` for the layout and
`display: flex; flex-direction: column; flex: 1; min-width: 0` for workspace-main.
The sidebar spans the viewport; the sticky header occupies only the content column. Remove
the page's previous header/sidebar. The home page is a working integration example.
Header and sidebar share state through NavigationService; pages do not pass role
or language inputs. Labels update from the canonical VI/EN dictionary.

Navigation uses authenticated session role codes and permission codes. Multiple
roles contribute a union of menu candidates, then required permissions filter the
items. ADMIN does not bypass missing permission claims. Unknown roles grant no menu.
MANAGER maps to department manager; branch management requires BRANCH_MANAGER.
Unimplemented pages are disabled, with an explicit localized label.

Role menus load from `/sidebar-configurations/current`; failures show a retry action
instead of substituting local defaults. Admins open **System Configuration** at
`/sidebar-settings` to select a role, toggle menu entries/children, reorder, preview,
restore defaults and save the shared configuration. Preview never changes the
admin's session roles. Backend checks active database admin assignments and uses a
version token to reject conflicting saves. See `docs/api/SIDEBAR_CONFIGURATION_CONTRACT.md`.

Display order uses Angular CDK drag-and-drop (mouse/touch), with keyboard up/down
on each drag handle. CDK is the only added runtime dependency and supplies touch
support and drag previews instead of browser-only drag events. Route selectors
configure per-role internal navigation from the supported route allowlist; blank
disables navigation. Group parents expand children rather than navigating.
Apply `AddSidebarRoutes` through DbMigrator after upgrading; existing saved menus
keep their catalog routes until an override is saved.

Apply `AddSidebarConfigurations` through DbMigrator and restart the API before
using this feature. Other sessions load changes on their next sidebar reload.

Menu filtering is presentation logic, not API authorization. Backend policies and
ownership checks remain authoritative. Existing profile/work-type/review pages are
demo/detail pages; these links do not imply a resource-list screen exists.

Icons are consistent inline SVG; the BPMS monogram is a neutral project mark rather
than an imitation bank logo. Styling follows frontend/docs/DESIGN.md, since the PNG
designs could not be inspected in this session. Existing pages can migrate using
the import pattern above; home is integrated in this change.
