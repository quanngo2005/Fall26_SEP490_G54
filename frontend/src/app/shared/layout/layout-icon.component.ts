import { Component, input } from '@angular/core';

const PATHS: Readonly<Record<string, string>> = {
  // Core nav icons matching reference design
  dashboard: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
  myWork:
    'M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2 M9 3h6v4H9z M9 12h6 M9 16h4',
  mySubmissions:
    'M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M23 21v-2a4 4 0 0 0-3-4 M16 3a4 4 0 0 1 0 8',
  myPerformance: 'M7 17l4-8 4 4 4-6 M3 20h18',
  reportsComplaints: 'M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z',
  notifications: 'M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9 M13.73 21a2 2 0 0 1-3.46 0',
  myProfile: 'M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2 M12 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8',
  people:
    'M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M23 21v-2a4 4 0 0 0-3-4 M16 3a4 4 0 0 1 0 8',
  organization:
    'M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M23 21v-2a4 4 0 0 0-3-4 M16 3a4 4 0 0 1 0 8',
  departments: 'M3 21h18 M5 21V3h14v18 M9 7h1 M14 7h1 M9 11h1 M14 11h1 M10 21v-6h4v6',
  jobPositions: 'M3 7h18v14H3z M8 7V3h8v4 M3 12h18 M10 12v3h4v-3',
  reportingLines: 'M12 3v18 M5 6l7-3 7 3 M5 18l7 3 7-3',
  employeeData: 'M4 3v18h17 M8 16v-5 M13 16V7 M18 16V4',
  teamWork:
    'M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M23 21v-2a4 4 0 0 0-3-4 M16 3a4 4 0 0 1 0 8',
  handoverDelegation: 'M7 16l-4-4 4-4 M21 12H3 M17 8l4 4-4 4',
  reviewVerify: 'M9 11l3 3 7-8 M20 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h10',
  teamPerformance: 'M4 3v18h17 M8 16v-5 M13 16V7 M18 16V4',
  targetsAndCampaigns:
    'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18 M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10 M12 11v2',
  branchWork:
    'M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2 M9 3h6v4H9z',
  branchPerformance: 'M3 21h18 M5 21V3h14v18 M9 7h1 M14 7h1 M9 11h1 M14 11h1 M10 21v-6h4v6',
  departmentPerformance: 'M4 3v18h17 M8 16v-5 M13 16V7 M18 16V4',
  reportingStructure: 'M9 5l7 7-7 7',
  verificationQueue:
    'M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2 M9 3h6v4H9z M9 12h6 M9 16h4',
  escalatedCases: 'M7 16l-4-4 4-4 M21 12H3 M17 8l4 4-4 4',
  auditReview: 'M12 3l8 3v6c0 5-8 9-8 9s-8-4-8-9V6z M8 12l3 3 5-6',
  riskEvents:
    'M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0 M12 9v4 M12 17h.01',
  reportsAnalytics: 'M18 20V10 M12 20V4 M6 20v-6',
  accessPolicies: 'M7 16l-4-4 4-4 M21 12H3 M17 8l4 4-4 4',
  systemConfiguration:
    'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M12 2v3 M12 19v3 M2 12h3 M19 12h3 M5 5l2 2 M17 17l2 2 M5 19l2-2 M17 7l2-2',
  systemAudit: 'M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9 M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8',
  accounts: 'M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2 M12 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8',
  rolesPermissions:
    'M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M23 21v-2a4 4 0 0 0-3-4 M16 3a4 4 0 0 1 0 8',
  // Legacy / utility
  tasks:
    'M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2 M9 3h6v4H9z M8 12h8 M8 16h6',
  chart: 'M4 3v18h17 M8 16v-5 M13 16V7 M18 16V4',
  check: 'M9 11l3 3 7-8 M20 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h10',
  building: 'M3 21h18 M5 21V3h14v18 M9 7h1 M14 7h1 M9 11h1 M14 11h1 M10 21v-6h4v6',
  briefcase: 'M3 7h18v14H3z M8 7V3h8v4 M3 12h18 M10 12v3h4v-3',
  target: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18 M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10 M12 11v2',
  shield: 'M12 3l8 3v6c0 5-8 9-8 9s-8-4-8-9V6z M8 12l3 3 5-6',
  settings:
    'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8 M12 2v3 M12 19v3 M2 12h3 M19 12h3 M5 5l2 2 M17 17l2 2 M5 19l2-2 M17 7l2-2',
  menu: 'M3 6h18 M3 12h18 M3 18h18',
  trash:
    'M3 6h18 M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2 M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6 M10 11v6 M14 11v6',
  chevron: 'M14 6l-6 6 6 6',
  chevronDown: 'M6 9l6 6 6-6',
  logout: 'M9 3H3v18h6 M14 7l5 5-5 5 M8 12h11',
  search: 'M21 21l-6-6m2-5a7 7 0 1 1-14 0 7 7 0 0 1 14 0',
  bell: 'M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9 M13.73 21a2 2 0 0 1-3.46 0',
  help: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20 M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3 M12 17h.01',
  externalLink: 'M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6 M15 3h6v6 M10 14L21 3',
};

@Component({
  selector: 'app-layout-icon',
  template:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path [attr.d]="path" /></svg>',
  styles: [
    ':host { display: inline-flex; width: 20px; height: 20px; flex-shrink: 0; } svg { width: 100%; height: 100%; }',
  ],
})
export class LayoutIconComponent {
  readonly name = input.required<string>();
  get path(): string {
    return PATHS[this.name()] ?? PATHS['dashboard'];
  }
}
