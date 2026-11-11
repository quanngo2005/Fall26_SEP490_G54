import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { DepartmentMember, DepartmentPosition, MemberStatus } from '../../core/department.models';
import { barWidth, formatSigned, formatValue, initials, toneOf } from './perf-display';

/**
 * The member table of SCR-PRF-02: one header row per job position (count, average, change and the weight of every
 * profile group), then its members with their group scores, total, a ± bar against the compare period and work counts.
 * Rows arrive grouped by position from the API; group columns are the union of the positions' root groups.
 */
@Component({
  selector: 'app-perf-member-table',
  imports: [RouterLink],
  templateUrl: './perf-member-table.component.html',
  styleUrl: './perf-member-table.component.scss',
})
export class PerfMemberTableComponent extends LocalizedComponent {
  readonly members = input.required<DepartmentMember[]>();
  readonly positions = input.required<DepartmentPosition[]>();
  readonly periodName = input<string | null>(null);
  readonly compareName = input<string | null>(null);

  /** Group columns: every root group name of the positions, in display order. */
  protected readonly columns = computed(() => {
    const order = new Map<string, number>();
    for (const group of this.positions().flatMap((p) => p.groups)) {
      order.set(
        group.name,
        Math.min(order.get(group.name) ?? group.displayOrder, group.displayOrder),
      );
    }
    return [...order.entries()].sort((a, b) => a[1] - b[1]).map(([name]) => name);
  });

  /** The page's rows split into the position sections they arrive in. */
  protected readonly sections = computed(() => {
    const sections: {
      position: DepartmentPosition | null;
      title: string;
      members: DepartmentMember[];
    }[] = [];
    for (const member of this.members()) {
      let section = sections.at(-1);
      if (!section || section.members[0].jobPositionId !== member.jobPositionId) {
        section = {
          position: this.positions().find((p) => p.jobPositionId === member.jobPositionId) ?? null,
          title: member.jobPositionTitle,
          members: [],
        };
        sections.push(section);
      }
      section.members.push(member);
    }
    return sections;
  });

  /** Largest |change| on the page: the ± bars are drawn relative to it. */
  private readonly maxChange = computed(() =>
    Math.max(0, ...this.members().map((m) => Math.abs(m.changeFromPrevious ?? 0))),
  );

  protected groupScore(member: DepartmentMember, name: string): number | null {
    return member.groups.find((g) => g.name === name)?.score ?? null;
  }

  protected isWeakest(member: DepartmentMember, name: string): boolean {
    return (
      member.weakestProfileItemId != null &&
      member.groups.find((g) => g.name === name)?.profileItemId === member.weakestProfileItemId
    );
  }

  protected weight(position: DepartmentPosition | null, name: string): number | null {
    return position?.groups.find((g) => g.name === name)?.weightPercent ?? null;
  }

  protected changeWidth(value: number | null): number {
    return barWidth(Math.abs(value ?? 0), this.maxChange());
  }

  protected score(value: number | null, decimals = 1): string {
    return formatValue(this.i18n.currentLanguage(), value, decimals);
  }

  protected signed(value: number | null): string {
    return formatSigned(this.i18n.currentLanguage(), value);
  }

  protected tone(value: number | null): string {
    return toneOf(value) ?? '';
  }

  protected initials(name: string): string {
    return initials(name);
  }

  protected statusLabel(status: MemberStatus): string {
    const text = this.localization.departmentPerformance;
    switch (status) {
      case 'ACTIVE':
        return text.statusActive;
      case 'ON_LEAVE':
        return text.statusOnLeave;
      case 'INACTIVE':
        return text.statusInactive;
      default:
        return text.statusTerminated;
    }
  }
}
