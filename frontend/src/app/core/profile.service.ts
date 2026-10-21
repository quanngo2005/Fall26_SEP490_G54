import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import { PhoneUpdate } from './profile.models';

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/me/profile`;

  /** Sets the phone number; a blank value clears it. No other profile field can be changed. */
  updatePhone(phone: string | null): Observable<PhoneUpdate> {
    return this.http
      .put<ApiResponse<PhoneUpdate>>(this.url, { phone })
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap(response: ApiResponse<PhoneUpdate>): PhoneUpdate {
    if (!response.success || response.data == null) throw new Error('Invalid profile response');
    return response.data;
  }
}
