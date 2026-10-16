import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../ui';

interface NavItem {
  readonly key: string;
  readonly label: string;
  readonly icon: string;
  /** Present only when the destination screen exists. */
  readonly route?: string;
}

interface NavGroup {
  readonly title: string;
  readonly items: readonly NavItem[];
}

const NAVIGATION: readonly NavGroup[] = [
  {
    title: 'TỔ CHỨC & PHÒNG BAN',
    items: [
      { key: 'departments', label: 'Phòng ban / Đơn vị', icon: '▦' },
      { key: 'org-chart', label: 'Vị trí / Chức danh', icon: '◫' },
    ],
  },
  {
    title: 'QUẢN TRỊ NHÂN SỰ',
    items: [
      { key: 'employees', label: 'Danh bạ Cán bộ', icon: '☰', route: '/employees' },
      { key: 'employee-profile', label: 'Hồ sơ cán bộ', icon: '▤', route: '/employee-profile' },
    ],
  },
  {
    title: 'HIỆU SUẤT & KPI',
    items: [
      { key: 'kpi', label: 'Khung chỉ số KPI', icon: '◔' },
      { key: 'review-verify', label: 'Thẩm định kết quả', icon: '✓', route: '/review-verify' },
    ],
  },
];

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent {
  readonly activeKey = input('');

  protected readonly navigation = NAVIGATION;
  protected readonly user = inject(AuthService).currentUser;
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  protected open(item: NavItem): void {
    if (item.route) {
      void this.router.navigateByUrl(item.route);
    } else {
      this.toast.info(`Màn hình "${item.label}" đang được phát triển.`);
    }
  }
}
