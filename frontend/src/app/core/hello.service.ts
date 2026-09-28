import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface HelloResponse {
  message: string;
}

@Injectable({ providedIn: 'root' })
export class HelloService {
  private readonly http = inject(HttpClient);

  getMessage(): Observable<HelloResponse> {
    return this.http.get<HelloResponse>(`${environment.apiUrl}/hello`);
  }
}
