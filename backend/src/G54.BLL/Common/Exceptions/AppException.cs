using G54.BLL.Common.Constants;

namespace G54.BLL.Common.Exceptions;

public class AppException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public IReadOnlyList<string>? Errors { get; }

    public AppException(
        string message,
        string errorCode = ErrorCodes.InternalError,
        int statusCode = 400,
        IReadOnlyList<string>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Errors = errors;
    }
}
