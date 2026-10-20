/** Standard paged payload carried inside `ApiResponse.data`. Mirrors G54.BLL.Common.Models.PagedResult<T>. */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
