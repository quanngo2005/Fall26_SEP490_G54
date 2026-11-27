import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import {
  QueueService,
  Lobby,
  Desk,
  ServiceCode,
  tellerServices,
  salesServices,
} from '../../core/queue.service';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';

@Component({
  selector: 'app-queue-lobby',
  imports: [FormsModule, DatePipe, DecimalPipe, HeaderComponent, SidebarComponent],
  templateUrl: './queue-lobby.component.html',
  styleUrl: './queue.scss',
})
export class QueueLobbyComponent extends LocalizedComponent {
  private readonly api = inject(QueueService);
  readonly lobby = signal<Lobby | null>(null);
  readonly saving = signal(false);
  readonly offline = signal(false);
  readonly notice = signal<'saved' | 'stale' | 'bad' | null>(null);
  edit: Desk | null = null;
  private seq = 0;
  constructor() {
    super();
    void this.refresh();
    const timer = setInterval(() => void this.refresh(), 5000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
  async refresh() {
    if (this.saving()) return;
    const seq = ++this.seq;
    try {
      const value = await firstValueFrom(this.api.lobby());
      if (seq === this.seq) {
        this.lobby.set(value);
        this.offline.set(false);
      }
    } catch {
      if (seq === this.seq) this.offline.set(true);
    }
  }
  minutes(from: string) {
    return Math.max(
      0,
      Math.floor((Date.parse(this.lobby()!.serverTime) - Date.parse(from)) / 60000),
    );
  }
  staffName(id: string | null) {
    return this.lobby()?.staff.find((s) => s.id === id)?.name ?? this.localization.queue.unassigned;
  }
  choices(position: string) {
    return position === 'SALES' ? salesServices : tellerServices;
  }
  configure(d: Desk) {
    this.edit = { ...d, services: [...d.services] };
    this.notice.set(null);
  }
  toggle(d: Desk, s: ServiceCode) {
    d.services = d.services.includes(s) ? d.services.filter((x) => x !== s) : [...d.services, s];
  }
  get total() {
    return this.lobby()?.daily.reduce((n, d) => n + d.count, 0) ?? 0;
  }
  async save(d: Desk) {
    if (this.saving()) return;
    this.seq++;
    this.saving.set(true);
    try {
      this.lobby.set(await firstValueFrom(this.api.save(d)));
      this.notice.set('saved');
      this.edit = null;
      this.offline.set(false);
    } catch (err) {
      this.notice.set(err instanceof HttpErrorResponse && err.status === 409 ? 'stale' : 'bad');
      this.edit = null;
    } finally {
      this.saving.set(false);
      void this.refresh();
    }
  }
}
