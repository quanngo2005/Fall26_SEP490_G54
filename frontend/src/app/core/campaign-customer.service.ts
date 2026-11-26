import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';
import {
  AllocateCustomersPayload,
  AllocateCustomersResult,
  CampaignCustomer,
  CampaignCustomerFilter,
  CampaignCustomerList,
  CustomerImportPreview,
  CustomerImportResult,
  MyCampaign,
  MyCustomerFilter,
  UpdateContactPayload,
} from './campaign-customer.models';
import { PagedResult } from './paged-result.model';

/** Campaign customers: manager side (`/campaigns/{id}/customers`) and employee side (`/my-campaigns`). */
@Injectable({ providedIn: 'root' })
export class CampaignCustomerService {
  private readonly http = inject(HttpClient);
  private readonly campaignsUrl = `${environment.apiUrl}/campaigns`;
  private readonly myUrl = `${environment.apiUrl}/my-campaigns`;

  list(campaignId: string, filter: CampaignCustomerFilter = {}): Observable<CampaignCustomerList> {
    return this.http
      .get<ApiResponse<CampaignCustomerList>>(this.customersUrl(campaignId), {
        params: this.params(filter),
      })
      .pipe(map((response) => this.unwrap(response)));
  }

  allocate(
    campaignId: string,
    payload: AllocateCustomersPayload,
  ): Observable<AllocateCustomersResult> {
    return this.http
      .post<ApiResponse<AllocateCustomersResult>>(
        `${this.customersUrl(campaignId)}/allocate`,
        payload,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  reassign(
    campaignId: string,
    customerId: string,
    employeeId: string,
  ): Observable<CampaignCustomer> {
    return this.http
      .put<ApiResponse<CampaignCustomer>>(
        `${this.customersUrl(campaignId)}/${encodeURIComponent(customerId)}/assignee`,
        { employeeId },
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  downloadTemplate(campaignId: string): Observable<Blob> {
    return this.http.get(`${this.customersUrl(campaignId)}/import/template`, {
      responseType: 'blob',
    });
  }

  importPreview(campaignId: string, file: File): Observable<CustomerImportPreview> {
    return this.http
      .post<ApiResponse<CustomerImportPreview>>(
        `${this.customersUrl(campaignId)}/import/preview`,
        this.fileForm(file),
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  importCommit(campaignId: string, file: File): Observable<CustomerImportResult> {
    return this.http
      .post<ApiResponse<CustomerImportResult>>(
        `${this.customersUrl(campaignId)}/import/commit`,
        this.fileForm(file),
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  myCampaigns(): Observable<MyCampaign[]> {
    return this.http
      .get<ApiResponse<MyCampaign[]>>(this.myUrl)
      .pipe(map((response) => this.unwrap(response)));
  }

  myCustomers(
    campaignId: string,
    filter: MyCustomerFilter = {},
  ): Observable<PagedResult<CampaignCustomer>> {
    return this.http
      .get<ApiResponse<PagedResult<CampaignCustomer>>>(
        `${this.myUrl}/${encodeURIComponent(campaignId)}/customers`,
        { params: this.params(filter) },
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  updateContact(
    campaignId: string,
    customerId: string,
    payload: UpdateContactPayload,
  ): Observable<CampaignCustomer> {
    return this.http
      .patch<ApiResponse<CampaignCustomer>>(
        `${this.myUrl}/${encodeURIComponent(campaignId)}/customers/${encodeURIComponent(customerId)}/contact`,
        payload,
      )
      .pipe(map((response) => this.unwrap(response)));
  }

  private customersUrl(campaignId: string): string {
    return `${this.campaignsUrl}/${encodeURIComponent(campaignId)}/customers`;
  }

  private fileForm(file: File): FormData {
    const form = new FormData();
    form.append('file', file, file.name);
    return form;
  }

  private params(filter: object): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '' && value !== false) {
        params = params.set(key, String(value));
      }
    }
    return params;
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error('Invalid campaign customer response');
    }
    return response.data;
  }
}
