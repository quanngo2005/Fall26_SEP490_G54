import { Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';
import { BranchTargetOverviewComponent } from './branch-target-overview.component';
import { DepartmentTargetOverviewComponent } from './department-target-overview.component';

/** Roles that see the whole branch; everybody else with `TARGET_MANAGE` sees their own department. */
const BRANCH_VIEW_ROLES = ['BRANCH_MANAGER', 'ADMIN'];

/** SCR-STRAT-03: branch managers see the branch tree, department managers their own allocation. */
@Component({
  selector: 'app-target-overview',
  imports: [
    HeaderComponent,
    SidebarComponent,
    BranchTargetOverviewComponent,
    DepartmentTargetOverviewComponent,
  ],
  templateUrl: './target-overview.component.html',
  styleUrls: ['../../shared/ui/workspace-page.scss'],
})
export class TargetOverviewComponent {
  protected readonly branchView = (inject(AuthService).currentUser()?.roles ?? []).some((role) =>
    BRANCH_VIEW_ROLES.includes(role.toUpperCase()),
  );
}
