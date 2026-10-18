/** Shared response envelope. Mirrors G54.BLL.Common.Models.ApiResponse<T>. See docs/api/API_CONVENTIONS.md. */
export interface ApiResponse<T> {
  success: boolean;
  status: number;
  data: T;
  message?: string | null;
  errorCode?: string | null;
  errors?: string[] | null;
  title?: string | null;
  traceId?: string | null;
  pagination?: unknown;
}

/** Standard paged payload carried inside `ApiResponse.data`. */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
