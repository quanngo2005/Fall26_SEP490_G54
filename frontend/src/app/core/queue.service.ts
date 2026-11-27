import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';

export type ServiceCode =
  'deposit' | 'withdrawal' | 'transfer' | 'account' | 'savings' | 'card' | 'advice';
export const tellerServices: ServiceCode[] = ['deposit', 'withdrawal', 'transfer', 'account'];
export const salesServices: ServiceCode[] = ['account', 'savings', 'card', 'advice'];
export const services = [...new Set([...tellerServices, ...salesServices])];
export const workTypes: Record<ServiceCode, string[]> = {
  deposit: ['GIAO_DICH'],
  withdrawal: ['GIAO_DICH'],
  transfer: ['GIAO_DICH'],
  account: ['MO_TAI_KHOAN'],
  savings: ['HUY_DONG'],
  card: ['THE_TD_CLASSIC', 'THE_TD_GOLD', 'THE_TD_PLATINUM'],
  advice: [],
};
export interface CounterState {
  counter: number | null;
  serverTime: string;
  options: string[];
  visit: {
    id: string;
    number: string;
    service: ServiceCode;
    state: 'called' | 'serving';
    calledAt: string;
    workType: string | null;
    workId: string | null;
  } | null;
}
export interface Command {
  requestId: string;
  action: string;
  visitId?: string;
  service?: ServiceCode;
  workType?: string;
}
export interface Desk {
  counter: number;
  position: string;
  employee: string | null;
  locked: boolean;
  revision: number;
  services: ServiceCode[];
  state: 'closed' | 'locked' | 'idle' | 'called' | 'serving';
  number: string | null;
  service: ServiceCode | null;
  calledAt: string | null;
}
export interface Lobby {
  serverTime: string;
  issuedToday: number;
  waitLimitMinutes: number;
  serviceLimitMinutes: number;
  desks: Desk[];
  staff: { id: string; name: string; position: string }[];
  waiting: { number: string; service: ServiceCode; issuedAt: string }[];
  daily: {
    employee: string;
    count: number;
    waitSeconds: number;
    serviceSeconds: number;
    score: number | null;
    ratings: number;
  }[];
}
function unpack<T>(r: ApiResponse<T>): T {
  if (r.data == null || !r.success) throw Error('Invalid queue response');
  return r.data;
}
@Injectable({ providedIn: 'root' })
export class QueueService {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiUrl + '/queue-counters';
  state() {
    return this.http.get<ApiResponse<CounterState>>(this.url).pipe(map(unpack));
  }
  command(cmd: Command) {
    return this.http.post<ApiResponse<CounterState>>(this.url + '/commands', cmd).pipe(map(unpack));
  }
  lobby() {
    return this.http.get<ApiResponse<Lobby>>(this.url + '/lobby').pipe(map(unpack));
  }
  save(d: Desk) {
    return this.http
      .put<ApiResponse<Lobby>>(this.url + '/lobby/desks/' + d.counter, {
        employee: d.employee,
        services: d.services,
        locked: d.locked,
        revision: d.revision,
      })
      .pipe(map(unpack));
  }
}
