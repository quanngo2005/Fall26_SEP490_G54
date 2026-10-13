namespace G54.BLL.Common.Models;

public sealed record PaginationMetadata
{
    public int PageIndex { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages { get; init; }
    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;

    public static PaginationMetadata Create(int pageIndex, int pageSize, int totalItems)
    {
        var safePageSize = pageSize <= 0 ? 20 : pageSize;
        return new PaginationMetadata
        {
            PageIndex = pageIndex <= 0 ? 1 : pageIndex,
            PageSize = safePageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)safePageSize),
        };
    }
}
