import { Directive, inject } from '@angular/core';
import { I18nService } from './i18n.service';

/** Shared reactive dictionary access for Angular templates. */
@Directive()
export abstract class LocalizedComponent {
  protected readonly i18n = inject(I18nService);

  get localization() {
    return this.i18n.localization;
  }

  get navigationGroups() {
    const nav = this.localization.nav;
    return [
      {
        id: 'personal',
        title: nav.personalWorkspace,
        items: [
          { id: 'overview', label: nav.overviewDashboard, icon: '▦', count: '' },
          { id: 'tasks', label: nav.myTasks, icon: '◉', count: '12' },
          { id: 'kpis', label: nav.myKpis, icon: '⌁', count: '' },
          { id: 'performance', label: nav.performanceReview, icon: '♧', count: '' },
          { id: 'handover', label: nav.handoverRequests, icon: '⟳', count: '1' },
          { id: 'appeals', label: nav.complaintsAppeals, icon: '⚒', count: '' },
        ],
      },
      {
        id: 'organization',
        title: nav.organization,
        items: [
          { id: 'departments', label: nav.departments, icon: '▦', count: '' },
          { id: 'positions', label: nav.jobPositions, icon: '♙', count: '' },
          { id: 'employees', label: nav.employeeProfiles, icon: '▣', count: '' },
        ],
      },
      {
        id: 'schedule',
        title: nav.notificationsSchedule,
        items: [
          { id: 'notifications', label: nav.operationalNotifications, icon: '⚑', count: '3' },
          { id: 'calendar', label: nav.workingSchedule, icon: '▣', count: '' },
        ],
      },
    ];
  }
}
