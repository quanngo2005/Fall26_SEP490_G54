import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { LocalizedComponent } from '../../core/i18n/localized.component';
import {
  QueueService,
  CounterState,
  Command,
  ServiceCode,
  services,
  workTypes,
} from '../../core/queue.service';
import { HeaderComponent, SidebarComponent } from '../../shared/layout';

@Component({
  selector: 'app-queue-counter',
  imports: [FormsModule, RouterLink, HeaderComponent, SidebarComponent],
  templateUrl: './queue-counter.component.html',
  styleUrl: './queue.scss',
})
export class QueueCounterComponent extends LocalizedComponent {
  private readonly api = inject(QueueService);
  readonly state = signal<CounterState | null>(null);
  readonly now = signal(Date.now());
  readonly busy = signal(false);
  readonly message = signal<'bad' | 'failed' | 'noDesk' | 'emptyQueue' | null>(null);
  readonly services = services;
  destination: ServiceCode = 'account';
  selected = '';
  private retry?: Command;
  private offset = 0;
  private seq = 0;
  constructor() {
    super();
    void this.refresh();
    const poll = setInterval(() => void this.refresh(), 5000);
    const clock = setInterval(() => this.now.set(Date.now() + this.offset), 1000);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(poll);
      clearInterval(clock);
    });
  }
  types(service: ServiceCode) {
    return workTypes[service].filter((t) => this.state()?.options.includes(t));
  }
  elapsed(at: string) {
    const sec = Math.max(0, Math.floor((this.now() - Date.parse(at)) / 1000));
    return Math.floor(sec / 60) + ':' + String(sec % 60).padStart(2, '0');
  }
  private apply(data: CounterState) {
    this.offset = Date.parse(data.serverTime) - Date.now();
    this.now.set(Date.now() + this.offset);
    this.state.set(data);
  }
  async refresh() {
    if (this.busy()) return;
    const seq = ++this.seq;
    try {
      const s = await firstValueFrom(this.api.state());
      if (seq === this.seq) {
        this.apply(s);
        if (this.message() === 'failed') this.message.set(null);
      }
    } catch {
      if (seq === this.seq) this.message.set('failed');
    }
  }
  async act(action: string) {
    if (this.busy()) return;
    this.seq++;
    this.busy.set(true);
    this.message.set(null);
    const v = this.state()?.visit;
    const service = action === 'convert' || action === 'transfer' ? this.destination : undefined;
    const types = this.types(service ?? v?.service ?? 'advice');
    const workType =
      action === 'convert' || (action === 'start' && !v?.workType)
        ? types.includes(this.selected)
          ? this.selected
          : types[0]
        : undefined;
    const cmd: Command = {
      requestId: crypto.randomUUID(),
      action,
      visitId: v?.id,
      service,
      workType,
    };
    if (
      this.retry &&
      JSON.stringify({ ...this.retry, requestId: '' }) === JSON.stringify({ ...cmd, requestId: '' })
    )
      cmd.requestId = this.retry.requestId;
    this.retry = cmd;
    try {
      this.apply(await firstValueFrom(this.api.command(cmd)));
      this.retry = undefined;
      this.selected = '';
    } catch (err) {
      const code = err instanceof HttpErrorResponse ? err.error?.errorCode : '';
      this.message.set(
        code === 'QUEUE_EMPTY' ? 'emptyQueue' : code === 'NO_FREE_COUNTER' ? 'noDesk' : 'bad',
      );
    } finally {
      this.busy.set(false);
    }
  }
}
