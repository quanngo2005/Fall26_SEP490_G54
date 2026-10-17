using G54.BLL.Common.Constants;

namespace G54.BLL.Common.Exceptions;

public class ConflictException : AppException
{
    public ConflictException(string message, string errorCode = ErrorCodes.Conflict)
        : base(message, errorCode, 409)
    {
    }
}
