import { Component } from '@angular/core';
import { HeaderComponent } from './header.component';
import { SidebarComponent } from './sidebar.component';

@Component({
  selector: 'app-workspace-layout',
  standalone: true,
  imports: [HeaderComponent, SidebarComponent],
  templateUrl: './workspace-layout.component.html',
  styleUrl: './workspace-layout.component.scss',
})
export class WorkspaceLayoutComponent {}
