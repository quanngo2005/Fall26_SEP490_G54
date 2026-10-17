namespace G54.BLL.Common.Models;

public class ApiResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? ErrorCode { get; init; }
    public IReadOnlyList<string>? Errors { get; init; }
    public string? TraceId { get; init; }
    public int? Status { get; init; }
    public string? Title { get; init; }

    public static ApiResponse Ok(string? message = null, string? traceId = null)
    {
        return new ApiResponse
        {
            Success = true,
            Status = 200,
            Message = message,
            TraceId = traceId,
        };
    }

    public static ApiResponse<T> Ok<T>(T data, string? message = null, string? traceId = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Status = 200,
            Data = data,
            Message = message,
            TraceId = traceId,
        };
    }

    public static ApiResponse<T> Ok<T>(
        T data,
        PaginationMetadata pagination,
        string? message = null,
        string? traceId = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Status = 200,
            Data = data,
            Pagination = pagination,
            Message = message,
            TraceId = traceId,
        };
    }

    public static ApiResponse Fail(
        string errorCode,
        string message,
        IReadOnlyList<string>? errors = null,
        string? traceId = null,
        int? status = 400,
        string? title = null)
    {
        return new ApiResponse
        {
            Success = false,
            Status = status,
            Title = title ?? errorCode,
            ErrorCode = errorCode,
            Message = message,
            Errors = errors,
            TraceId = traceId,
        };
    }

    public static ApiResponse<T> Fail<T>(
        string errorCode,
        string message,
        IReadOnlyList<string>? errors = null,
        string? traceId = null,
        int? status = 400,
        string? title = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Status = status,
            Title = title ?? errorCode,
            ErrorCode = errorCode,
            Message = message,
            Errors = errors,
            TraceId = traceId,
        };
    }
}

public sealed class ApiResponse<T> : ApiResponse
{
    public T? Data { get; init; }
    public PaginationMetadata? Pagination { get; init; }
}
