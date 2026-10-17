using G54.BLL.Common.Constants;

namespace G54.BLL.Common.Exceptions;

public class ForbiddenException : AppException
{
    public ForbiddenException(string? message = null)
        : base(message ?? ErrorCodes.GetDefaultMessage(ErrorCodes.Forbidden), ErrorCodes.Forbidden, 403)
    {
    }
}
